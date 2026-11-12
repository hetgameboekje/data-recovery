using Microsoft.Extensions.DependencyInjection;
using RecoveryBLL.Dtos;
using RecoveryBLL.Exceptions;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;
using RecoveryBLL.Services;
using RecoveryBLL.Validation;
using RecoveryDAL.Storage;

namespace RecoveryBLL.Tests;

/// <summary>The toolkit must never scan or write the Windows drive as a whole, system folders or AppData.</summary>
public class PathSafetyTests
{
    sealed class FixedLocations : IProtectedLocations
    {
        public string SystemDrive => @"C:\";
        public IReadOnlyList<string> NoGoFolders => [@"C:\Windows", @"C:\ProgramData", @"C:\Users\bob\AppData", @"C:\Users\eve\AppData"];
        public string Normalize(string path) { var f = Path.GetFullPath(path); return f.Length > 3 ? f.TrimEnd('\\') : f; }
    }

    [Theory]
    [InlineData(@"C:\")]                          // the Windows drive as a whole
    [InlineData(@"C:")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\Temp")]
    [InlineData(@"C:\ProgramData\Vendor")]
    [InlineData(@"C:\Users")]                     // contains every AppData
    [InlineData(@"C:\Users\bob")]                 // a profile root contains its AppData
    [InlineData(@"C:\Users\bob\AppData\Local")]
    [InlineData(@"C:\Users\bob\Pictures\..\AppData")]
    [InlineData(@"\\server\share")]
    [InlineData("")]
    public void Refused(string path)
    {
        Assert.Throws<RecoveryServiceException>(() => PathSafetyValidator.Validate(path, PathUse.Destination, new FixedLocations()));
        Assert.Throws<RecoveryServiceException>(() => PathSafetyValidator.Validate(path, PathUse.ScanSource, new FixedLocations()));
    }

    [Theory]
    [InlineData(@"C:\Recovery", @"C:\Recovery")]
    [InlineData(@"C:\Users\bob\Pictures", @"C:\Users\bob\Pictures")]
    [InlineData(@"C:\Users\bob\Pictures\", @"C:\Users\bob\Pictures")]
    [InlineData(@"D:\", @"D:\")]                  // other drives may be used whole
    [InlineData(@"D:", @"D:\")]
    [InlineData(@"H:\Backup", @"H:\Backup")]
    public void Allowed(string path, string expected) =>
        Assert.Equal(expected, PathSafetyValidator.Validate(path, PathUse.Destination, new FixedLocations()), ignoreCase: true);

    [Fact]
    public void The_real_Windows_list_protects_this_PC()
    {
        var real = new WindowsProtectedLocations();
        var system = real.SystemDrive;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] refused =
        [
            system,
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
            Path.Combine(profile, "AppData", "Roaming", "Something"),
            profile,
            Path.GetDirectoryName(profile)!,
        ];
        foreach (var p in refused)
            Assert.Throws<RecoveryServiceException>(() => PathSafetyValidator.Validate(p, PathUse.Destination, real));

        PathSafetyValidator.Validate(Path.Combine(profile, "Pictures", "Recovered"), PathUse.ScanSource, real);
        PathSafetyValidator.Validate(Path.Combine(system, "RecoveryTarget-does-not-exist"), PathUse.Destination, real);
    }

    [Fact]
    public void Scanning_the_Windows_drive_or_AppData_is_refused_before_anything_is_read()
    {
        using var d = new TempDir();
        using var sp = TestHost.Create(d.Path, new ListLog(), realProtection: true);
        var scan = sp.GetRequiredService<ScanService>();
        Assert.Throws<RecoveryServiceException>(() => scan.Scan(new ScanRequest(@"C:\", []), null, CancellationToken.None));
        Assert.Throws<RecoveryServiceException>(() => scan.Scan(new ScanRequest(d.Path, []), null, CancellationToken.None));   // %TEMP% is in AppData
    }

    [Fact]
    public void Copy_to_the_Windows_drive_root_or_AppData_is_refused()
    {
        using var d = new TempDir();
        using var sp = TestHost.Create(d.Path, new ListLog(), realProtection: true);
        var service = sp.GetRequiredService<ConsolidationService>();
        Assert.Throws<RecoveryServiceException>(() => service.Plan(new ConsolidateOptions([new DestinationRequest(@"C:\")]), null, CancellationToken.None));
        Assert.Throws<RecoveryServiceException>(() => service.Plan(new ConsolidateOptions([new DestinationRequest(d.Path)]), null, CancellationToken.None));
    }
}

/// <summary>What a crashed run held must be given back on the next start.</summary>
public class RunStateTests
{
    sealed class FakeProcesses : IProcessInfo
    {
        public HashSet<int> Alive { get; } = [];
        public int CurrentId => 1;
        public long CurrentStartTicks => 100;
        public bool IsAlive(int processId, long startTicks) => processId == CurrentId || Alive.Contains(processId);
    }

    sealed class FakeWsl : IWslGateway
    {
        public List<int> Unmounted { get; } = [];
        public bool Fail { get; set; }
        public bool IsInstalled() => true;
        public IReadOnlyList<string> GetDistributions() => ["Ubuntu"];
        public string MountReadOnly(int diskNumber, int partition, string fileSystem) => throw new NotSupportedException();
        public void Unmount(int diskNumber) { if (Fail) throw new IOException("access denied"); Unmounted.Add(diskNumber); }
        public VolumeIdentity? GetMountIdentity(string windowsPath, int diskNumber, int partition, string fileSystem) => null;
    }

    sealed class Elevation(bool elevated) : IElevationInfo { public bool IsElevated => elevated; }

    static (RunStateService Service, RunStateFile Store, FakeProcesses Processes, FakeWsl Wsl) Create(string dir, bool elevated = true, IDeletionConfirmer? confirmer = null)
    {
        var store = new RunStateFile(Path.Combine(dir, "state"));
        var processes = new FakeProcesses();
        var wsl = new FakeWsl();
        return (new RunStateService(store, processes, wsl, new Elevation(elevated), new FileStore(), confirmer ?? new RefuseAllDeletions(), new ListLog()),
                store, processes, wsl);
    }

    static void AddLease(RunStateFile store, LeaseKind kind, string target, int pid) =>
        store.Update(l => l.Add(new RunLease(kind, target, pid, 5, "2026-11-12T10:00:00.000Z")));

    static string CrashedCopy(TempDir d, RunStateFile store)
    {
        var dest = Path.Combine(d.Path, "dest");
        d.File(@"dest\Photos\a.jpg", [1, 2, 3]);
        d.File(@"dest\Photos\b.jpg.rtpart", [1]);
        d.File(@"dest\_Duplicates\x\c.mp4.rtpart", [1, 2]);
        AddLease(store, LeaseKind.CopyDestination, dest, pid: 4242);   // process 4242 is gone
        return dest;
    }

    [Fact]
    public void Start_up_only_lists_leftovers_of_a_crashed_copy_and_deletes_nothing()
    {
        using var d = new TempDir();
        var (service, store, _, _) = Create(d.Path, confirmer: new TestConfirmer(true));
        var dest = CrashedCopy(d, store);

        var r = service.ReleaseStale();
        Assert.Equal(0, r.TempFilesDeleted);
        Assert.Equal(2, r.Leftovers.Count);
        Assert.Equal(2, Directory.GetFiles(dest, "*.rtpart", SearchOption.AllDirectories).Length);
        Assert.Single(service.Current());                                // kept as a reminder until they are gone
    }

    [Fact]
    public void Leftovers_are_deleted_only_with_the_right_code_and_the_final_confirmation()
    {
        using var d = new TempDir();
        var no = new TestConfirmer(false);
        var (refusing, store, _, _) = Create(d.Path, confirmer: no);
        var dest = CrashedCopy(d, store);
        var code = refusing.FindLeftovers(null).Code;

        Assert.Equal(0, refusing.DeleteLeftovers(null, null).TempFilesDeleted);        // step 1 missing
        Assert.Equal(0, refusing.DeleteLeftovers(null, "ABCDEF").TempFilesDeleted);    // wrong code
        Assert.Equal(0, no.Asked);
        Assert.Equal(0, refusing.DeleteLeftovers(null, code).TempFilesDeleted);        // step 2 says no
        Assert.Equal(1, no.Asked);
        Assert.Equal(2, Directory.GetFiles(dest, "*.rtpart", SearchOption.AllDirectories).Length);

        var yes = new TestConfirmer(true);
        var (service, _, _, _) = Create(d.Path, confirmer: yes);
        var r = service.DeleteLeftovers(null, code);
        Assert.Equal(2, r.TempFilesDeleted);
        Assert.True(File.Exists(Path.Combine(dest, @"Photos\a.jpg")));               // real files are never touched
        Assert.Empty(Directory.GetFiles(dest, "*.rtpart", SearchOption.AllDirectories));
        Assert.Empty(service.Current());                                             // destination released
    }

    [Fact]
    public void A_code_for_an_older_list_does_not_delete_a_changed_list()
    {
        using var d = new TempDir();
        var (service, store, _, _) = Create(d.Path, confirmer: new TestConfirmer(true));
        var dest = CrashedCopy(d, store);
        var code = service.FindLeftovers(null).Code;
        File.WriteAllBytes(Path.Combine(dest, @"Photos\b.jpg.rtpart"), [1, 2, 3, 4, 5]);   // changed after it was listed

        var r = service.DeleteLeftovers(null, code);
        Assert.Equal(0, r.TempFilesDeleted);
        Assert.NotEmpty(r.Problems);
        Assert.Equal(2, Directory.GetFiles(dest, "*.rtpart", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void Deletion_codes_belong_to_exactly_one_list()
    {
        var a = new DeletionBatch([new DeletionCandidate(@"H:\x.rtpart", 5, "r"), new DeletionCandidate(@"H:\y.rtpart", 6, "r")]);
        var sameOtherOrder = new DeletionBatch([new DeletionCandidate(@"h:\Y.rtpart", 6, "r"), new DeletionCandidate(@"H:\x.rtpart", 5, "r")]);
        var bigger = new DeletionBatch([new DeletionCandidate(@"H:\x.rtpart", 5, "r"), new DeletionCandidate(@"H:\y.rtpart", 7, "r")]);
        Assert.Equal(a.Code, sameOtherOrder.Code);
        Assert.NotEqual(a.Code, bigger.Code);
        Assert.True(a.IsApprovedBy(a.Code.ToLowerInvariant()));
        Assert.False(a.IsApprovedBy(bigger.Code));
        Assert.False(a.IsApprovedBy(null));
        Assert.False(DeletionBatch.Empty.IsApprovedBy(""));
    }

    [Fact]
    public void A_Linux_disk_left_mounted_is_unmounted_or_kept_on_the_list_when_that_needs_admin()
    {
        using var d = new TempDir();
        var (service, store, _, wsl) = Create(d.Path);
        AddLease(store, LeaseKind.WslMount, "3", pid: 4242);
        service.ReleaseStale();
        Assert.Equal([3], wsl.Unmounted);
        Assert.Empty(service.Current());

        using var d2 = new TempDir();
        var (notAdmin, store2, _, wsl2) = Create(d2.Path, elevated: false);
        wsl2.Fail = true;
        AddLease(store2, LeaseKind.WslMount, "3", pid: 4242);
        var r = notAdmin.ReleaseStale();
        Assert.Single(r.Problems);
        Assert.Single(notAdmin.Current());                              // stays on the list until it is really free
    }

    [Fact]
    public void A_destination_in_use_by_a_live_run_is_refused_and_left_alone()
    {
        using var d = new TempDir();
        var dest = Path.Combine(d.Path, "dest");
        d.File(@"dest\busy.jpg.rtpart", [1]);
        var (service, store, processes, _) = Create(d.Path);
        processes.Alive.Add(77);
        AddLease(store, LeaseKind.CopyDestination, dest, pid: 77);

        Assert.Throws<RecoveryServiceException>(() => service.Hold(LeaseKind.CopyDestination, dest + "\\"));
        var problems = new List<string>();
        Assert.True(service.FindLeftovers([dest], problems).IsEmpty);      // a live run's .rtpart is never offered for deletion
        Assert.Single(problems);
        Assert.True(File.Exists(Path.Combine(dest, "busy.jpg.rtpart")));
        Assert.Single(service.ReleaseStale().StillHeld);
    }

    [Fact]
    public void A_read_that_hangs_is_cut_off_as_a_read_timeout()
    {
        using var slow = new SlowStream();
        using var s = new TimeoutStream(slow, TimeSpan.FromMilliseconds(200), "test");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Throws<ReadTimeoutException>(() => s.ReadExactly(new byte[10]));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
    }

    /// <summary>A "disk" whose reads never finish unless cancelled (like a sector the drive keeps retrying).</summary>
    sealed class SlowStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    [Fact]
    public void Hold_and_release_by_this_process_leave_the_journal_empty()
    {
        using var d = new TempDir();
        var (service, _, _, _) = Create(d.Path);
        service.Hold(LeaseKind.CopyDestination, @"H:\Backup");
        service.Hold(LeaseKind.CopyDestination, @"H:\Backup");          // re-entrant for the same process
        Assert.Single(service.Current());
        service.Release(LeaseKind.CopyDestination, @"H:\Backup\");
        Assert.Empty(service.Current());
    }
}

/// <summary>Disks get unplugged (loose cable, wrong one pulled, USB power): that must never damage the catalog.</summary>
public class UnplugTests
{
    [Fact]
    public void Unplugging_during_fingerprinting_leaves_the_files_untouched_and_the_next_run_finishes()
    {
        using var d = new TempDir();
        d.File(@"a\x.bin", TestHost.Random(5000, 1));
        d.File(@"a\y.bin", TestHost.Random(5000, 2));          // same size: both need a quick hash
        var source = Path.Combine(d.Path, "a");
        var reader = new HookedReader();
        var log = new ListLog();
        using var sp = TestHost.Create(d.Path, log, configure: s => s.AddSingleton<IFileContentReader>(reader));
        var scan = sp.GetRequiredService<ScanService>().Scan(
            new ScanRequest(source, [], new VolumeIdentity("AAAA", 1_000_000_000, "A", "NTFS", source + "\\")), null, CancellationToken.None);
        reader.BeforeOpen = _ => { if (Directory.Exists(source)) Directory.Move(source, source + "-unplugged"); };

        var hash = sp.GetRequiredService<FingerprintService>();
        var first = hash.Run(new FingerprintOptions(), null, CancellationToken.None, new HashSet<long> { scan.DiskId });
        Assert.False(first.Single(s => s.Stage == "Quick hash").Completed);
        Assert.Contains(log.Lines, l => l.Contains("unplugged"));

        Directory.Move(source + "-unplugged", source);         // plugged back in
        reader.BeforeOpen = null;
        var files = sp.GetRequiredService<IConsolidationRepository>().GetPlanFiles();
        Assert.Equal(2, files.Count);                          // not marked missing
        Assert.All(files, f => Assert.Equal(PlanFileKind.Pending, f.Kind));
        Assert.Equal(0, sp.GetRequiredService<ReportService>().GetReport().Disks.Sum(x => x.ErrorFiles));   // not marked unreadable

        var second = hash.Run(new FingerprintOptions(), null, CancellationToken.None, new HashSet<long> { scan.DiskId });
        Assert.Equal(2, second.Single(s => s.Stage == "Quick hash").Files);
        Assert.All(second, s => Assert.True(s.Completed));
    }

    [Fact]
    public void A_catalog_on_the_Windows_drive_gives_a_warning_when_that_drive_is_scanned()
    {
        using var d = new TempDir();
        d.File(@"src\a.txt", [1, 2, 3]);
        var log = new ListLog();
        using var sp = TestHost.Create(d.Path, log);            // catalog.db lives in %TEMP% on the Windows drive
        sp.GetRequiredService<ScanService>().Scan(new ScanRequest(Path.Combine(d.Path, "src"), []), null, CancellationToken.None);
        Assert.Contains(log.Lines, l => l.StartsWith("WARN") && l.Contains("Windows drive") && l.Contains("DELETED"));
    }
}
