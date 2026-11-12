using Microsoft.Extensions.DependencyInjection;
using RecoveryBLL.Exceptions;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;
using RecoveryBLL.Services;

namespace RecoveryBLL.Tests;

/// <summary>Phase 2 (copy plan + copy) through the real BLL + DAL. Folders stand in for disks via an identity override.</summary>
public class ConsolidationTests
{
    static VolumeIdentity Vol(string serial, string root) =>
        new(serial, 1_000_000_000, "T" + serial, "NTFS", root.EndsWith('\\') ? root : root + "\\");

    static ScanResult ScanAs(ServiceProvider sp, string folder, string serial) =>
        sp.GetRequiredService<ScanService>().Scan(new ScanRequest(folder, [], Vol(serial, folder)), null, CancellationToken.None);

    static ConsolidateOptions Options(string dest, long? max = null, bool duplicates = true, bool allowPending = false, string? approve = null) =>
        new([new DestinationRequest(dest, max, Vol("DST", dest))], duplicates, ReserveBytes: 0, AllowPending: allowPending, Verify: true,
            ApproveDeleteCode: approve, WaitForUnplugged: false) { UnplugWait = TimeSpan.FromSeconds(20) };

    static string Write(TempDir d, string relative, byte[] content, DateTime modifiedUtc)
    {
        var p = d.File(relative, content);
        File.SetLastWriteTimeUtc(p, modifiedUtc);
        return p;
    }

    static long Alloc(long size) => (size + 4095) / 4096 * 4096;

    [Fact]
    public void Plan_labels_keep_duplicate_and_older_version_then_copy_and_rerun_finds_everything_there()
    {
        using var d = new TempDir();
        using var sp = TestHost.Create(d.Path, new ListLog());
        FullScenario(d, sp);
    }

    /// <summary>The same scenario on SQL Server LocalDB. Runs only when RT_TEST_SQLSERVER is set.</summary>
    [Fact]
    public void Sql_server_provider_plans_and_copies_the_same()
    {
        if (Environment.GetEnvironmentVariable("RT_TEST_SQLSERVER") is not { Length: > 0 }) return;
        using var d = new TempDir();
        TestHost.WithLocalDb(d, sp => FullScenario(d, sp));
    }

    static void FullScenario(TempDir d, ServiceProvider sp)
    {
        var photo = TestHost.Random(300_000, 1);
        Write(d, @"a\Photos\holiday.jpg", photo, new DateTime(2019, 7, 1, 0, 0, 0, DateTimeKind.Utc));
        Write(d, @"a\backup\holiday copy.jpg", photo, new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));     // identical, newer
        Write(d, @"a\docs\report.txt", TestHost.Random(4000, 2), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));  // older version
        Write(d, @"a\unique.bin", TestHost.Random(7777, 3), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Write(d, @"b\docs\report.txt", TestHost.Random(4100, 4), new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc));  // newer version
        var dest = Path.Combine(d.Path, "dest");
        Directory.CreateDirectory(dest);

        ScanAs(sp, Path.Combine(d.Path, "a"), "AAAA");
        ScanAs(sp, Path.Combine(d.Path, "b"), "BBBB");
        var service = sp.GetRequiredService<ConsolidationService>();

        var plan = service.Plan(Options(dest), null, CancellationToken.None);
        PlanItem Item(string path) => plan.Items.Single(i => i.SourcePath.Equals(path, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(CopyAction.Keep, Item(@"Photos\holiday.jpg").Action);
        Assert.Equal(@"Photos\holiday.jpg", Item(@"Photos\holiday.jpg").DestPath);          // category not repeated
        Assert.Equal(CopyAction.Duplicate, Item(@"backup\holiday copy.jpg").Action);
        Assert.Equal(@"_Duplicates\TAAAA_AAAA\backup\holiday copy.jpg", Item(@"backup\holiday copy.jpg").DestPath);
        var reports = plan.Items.Where(i => i.SourcePath == @"docs\report.txt").ToList();
        var newer = reports.Single(i => i.Size == 4100);
        var older = reports.Single(i => i.Size == 4000);
        Assert.Equal(CopyAction.Keep, newer.Action);
        Assert.Equal(CopyAction.OlderVersion, older.Action);
        Assert.StartsWith(@"_OlderVersions\TAAAA_AAAA\", older.DestPath);
        Assert.Equal(CopyAction.Keep, Item("unique.bin").Action);
        Assert.Equal(5, plan.FilesToCopy);
        Assert.Equal(0, plan.PendingFiles);
        Assert.True(plan.UniqueFits);
        Assert.False(File.Exists(Path.Combine(dest, @"Photos\holiday.jpg")));                // a plan writes nothing

        var result = service.Execute(Options(dest), null, CancellationToken.None);
        Assert.True(result.Completed);
        Assert.Equal(5, result.Copied);
        Assert.Equal(0, result.Errors);
        var copied = Path.Combine(dest, @"Photos\holiday.jpg");
        Assert.Equal(photo, File.ReadAllBytes(copied));
        Assert.Equal(new DateTime(2019, 7, 1, 0, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(copied));
        Assert.Equal(4100, new FileInfo(Path.Combine(dest, newer.DestPath!)).Length);
        Assert.Empty(Directory.GetFiles(dest, "*.rtpart", SearchOption.AllDirectories));

        var again = service.Plan(Options(dest), null, CancellationToken.None);
        Assert.Equal(0, again.FilesToCopy);
        Assert.All(again.Items, i => Assert.Equal(CopyAction.AlreadyThere, i.Action));
    }

    [Fact]
    public void Unique_files_get_the_space_first_and_duplicates_only_what_is_left()
    {
        using var d = new TempDir();
        var shared = TestHost.Random(50_000, 5);
        var t = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Write(d, @"a\one.bin", shared, t);
        Write(d, @"a\two.bin", shared, t.AddDays(1));
        Write(d, @"a\three.bin", TestHost.Random(30_000, 6), t);
        var dest = Path.Combine(d.Path, "dest");
        Directory.CreateDirectory(dest);
        using var sp = TestHost.Create(d.Path, new ListLog());
        ScanAs(sp, Path.Combine(d.Path, "a"), "AAAA");
        var service = sp.GetRequiredService<ConsolidationService>();

        var exact = service.Plan(Options(dest, Alloc(50_000) + Alloc(30_000)), null, CancellationToken.None);
        Assert.True(exact.UniqueFits);
        Assert.Equal(2, exact.Files(CopyAction.Keep));
        var dup = exact.Items.Single(i => i.Action == CopyAction.NoRoom);
        Assert.Equal(PlanNotes.DuplicateNoRoom, dup.Note);

        var off = service.Plan(Options(dest, Alloc(50_000) + Alloc(30_000), duplicates: false), null, CancellationToken.None);
        Assert.Equal(1, off.Files(CopyAction.SkippedDuplicate));
        Assert.Equal(0, off.Files(CopyAction.NoRoom));

        var tooSmall = service.Plan(Options(dest, Alloc(50_000)), null, CancellationToken.None);
        Assert.False(tooSmall.UniqueFits);
    }

    static string EvictionSetup(TempDir d)
    {
        var t = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var kept = TestHost.Random(100_000, 7);
        Write(d, @"dest\Photos\kept.jpg", kept, t);
        Write(d, @"dest\_Duplicates\old\kept copy.jpg", kept, t);                            // evictable: also kept outside
        Write(d, @"dest\_Duplicates\old\lonely.jpg", TestHost.Random(120_000, 8), t);         // only copy: never evicted
        Write(d, @"a\new.bin", TestHost.Random(50_000, 9), t);
        return Path.Combine(d.Path, "dest");
    }

    [Fact]
    public void Old_duplicates_are_evicted_only_after_both_steps_and_never_the_last_copy()
    {
        using var d = new TempDir();
        var dest = EvictionSetup(d);
        var yes = new TestConfirmer(true);
        using var sp = TestHost.Create(d.Path, new ListLog(), configure: s => s.AddSingleton<IDeletionConfirmer>(yes));
        ScanAs(sp, Path.Combine(d.Path, "a"), "AAAA");
        var service = sp.GetRequiredService<ConsolidationService>();

        var plan = service.Plan(Options(dest, max: 10_000), null, CancellationToken.None);
        Assert.True(plan.UniqueFits);
        var eviction = Assert.Single(plan.Evictions);
        Assert.EndsWith("kept copy.jpg", eviction.Path);
        var deletion = Assert.Single(plan.Deletions.Files);
        Assert.EndsWith("kept copy.jpg", deletion.Path);
        Assert.Equal(6, plan.Deletions.Code.Length);

        // Step 1 missing (no code) or wrong: nothing deleted, the file that needs the room waits; step 2 is not even asked.
        foreach (var code in new string?[] { null, "000000" })
        {
            var refused = service.Execute(Options(dest, max: 10_000, approve: code), null, CancellationToken.None);
            Assert.Equal(0, refused.Evicted);
            Assert.Equal(1, refused.DeletionsNotApproved);
            Assert.Equal(0, refused.Copied);
            Assert.True(File.Exists(Path.Combine(dest, @"_Duplicates\old\kept copy.jpg")));
        }
        Assert.Equal(0, yes.Asked);

        // Both steps: deleted, then copied.
        var result = service.Execute(Options(dest, max: 10_000, approve: plan.Deletions.Code.ToLowerInvariant()), null, CancellationToken.None);
        Assert.Equal(1, yes.Asked);
        Assert.Equal(1, result.Copied);
        Assert.Equal(1, result.Evicted);
        Assert.False(File.Exists(Path.Combine(dest, @"_Duplicates\old\kept copy.jpg")));
        Assert.True(File.Exists(Path.Combine(dest, @"_Duplicates\old\lonely.jpg")));
        Assert.True(File.Exists(Path.Combine(dest, @"Photos\kept.jpg")));
        Assert.True(File.Exists(Path.Combine(dest, @"Other\new.bin")));
    }

    [Fact]
    public void Approved_eviction_is_still_kept_when_the_final_confirmation_says_no()
    {
        using var d = new TempDir();
        var dest = EvictionSetup(d);
        var no = new TestConfirmer(false);
        using var sp = TestHost.Create(d.Path, new ListLog(), configure: s => s.AddSingleton<IDeletionConfirmer>(no));
        ScanAs(sp, Path.Combine(d.Path, "a"), "AAAA");
        var service = sp.GetRequiredService<ConsolidationService>();
        var code = service.Plan(Options(dest, max: 10_000), null, CancellationToken.None).Deletions.Code;

        var result = service.Execute(Options(dest, max: 10_000, approve: code), null, CancellationToken.None);
        Assert.Equal(1, no.Asked);
        Assert.Equal(0, result.Evicted);
        Assert.Equal(0, result.Copied);
        Assert.True(File.Exists(Path.Combine(dest, @"_Duplicates\old\kept copy.jpg")));
    }

    [Fact]
    public void A_source_disk_unplugged_during_the_copy_is_waited_for_and_the_copy_continues()
    {
        using var d = new TempDir();
        var t = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 4; i++) Write(d, $@"a\file{i}.bin", TestHost.Random(10_000 + i, 20 + i), t);   // unique sizes: never hashed
        var dest = Path.Combine(d.Path, "dest");
        Directory.CreateDirectory(dest);
        var source = Path.Combine(d.Path, "a");
        var reader = new HookedReader();
        using var sp = TestHost.Create(d.Path, new ListLog(), configure: s => s.AddSingleton<IFileContentReader>(reader));
        ScanAs(sp, source, "AAAA");

        int opens = 0;
        Task? replug = null;
        reader.BeforeOpen = path =>
        {
            if (Interlocked.Increment(ref opens) != 2) return;                 // unplug while copying the 2nd file
            Directory.Move(source, source + "-unplugged");
            replug = Task.Delay(1500).ContinueWith(_ => Directory.Move(source + "-unplugged", source));
        };
        var options = Options(dest) with { WaitForUnplugged = true };
        var result = sp.GetRequiredService<ConsolidationService>().Execute(options, null, CancellationToken.None);
        replug?.Wait();

        Assert.True(result.Completed);
        Assert.Equal(4, result.Copied);
        Assert.Equal(0, result.Errors);
        Assert.Equal(4, Directory.GetFiles(Path.Combine(dest, "Other")).Length);
    }

    [Fact]
    public void Without_waiting_an_unplugged_source_disk_is_skipped_and_its_files_wait()
    {
        using var d = new TempDir();
        var t = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 3; i++) Write(d, $@"a\file{i}.bin", TestHost.Random(10_000 + i, 30 + i), t);
        var dest = Path.Combine(d.Path, "dest");
        Directory.CreateDirectory(dest);
        var source = Path.Combine(d.Path, "a");
        var reader = new HookedReader();
        using var sp = TestHost.Create(d.Path, new ListLog(), configure: s => s.AddSingleton<IFileContentReader>(reader));
        ScanAs(sp, source, "AAAA");
        reader.BeforeOpen = _ => { if (Directory.Exists(source)) Directory.Move(source, source + "-unplugged"); };

        var result = sp.GetRequiredService<ConsolidationService>().Execute(Options(dest), null, CancellationToken.None);
        Assert.Equal(0, result.Copied);
        Assert.Equal(3, result.WaitingForDisk);
        Assert.Equal(0, result.Errors);
        Assert.Empty(Directory.GetFiles(dest, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Destination_paths_over_260_characters_are_copied_and_reported()
    {
        using var d = new TempDir();
        var deep = string.Join('\\', Enumerable.Range(0, 12).Select(i => $"folder-with-a-long-name-{i:00}"));
        Write(d, $@"a\{deep}\photo.bin", TestHost.Random(9_999, 40), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var dest = Path.Combine(d.Path, "dest");
        Directory.CreateDirectory(dest);
        using var sp = TestHost.Create(d.Path, new ListLog());
        ScanAs(sp, Path.Combine(d.Path, "a"), "AAAA");
        var service = sp.GetRequiredService<ConsolidationService>();

        var plan = service.Plan(Options(dest), null, CancellationToken.None);
        Assert.Equal(1, plan.LongPaths);
        Assert.Contains(plan.Warnings, w => w.Contains("260"));
        Assert.Equal(1, service.Execute(Options(dest), null, CancellationToken.None).Copied);
    }

    [Fact]
    public void Copy_refuses_while_files_on_an_unplugged_disk_are_not_fingerprinted()
    {
        using var d = new TempDir();
        var t = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Write(d, @"a\x.bin", TestHost.Random(5000, 10), t);
        Write(d, @"b\y.bin", TestHost.Random(5000, 11), t);                                   // same size: needs a hash
        var dest = Path.Combine(d.Path, "dest");
        Directory.CreateDirectory(dest);
        using var sp = TestHost.Create(d.Path, new ListLog());
        ScanAs(sp, Path.Combine(d.Path, "a"), "AAAA");
        ScanAs(sp, Path.Combine(d.Path, "b"), "BBBB");
        Directory.Move(Path.Combine(d.Path, "b"), Path.Combine(d.Path, "b-unplugged"));        // disk B is now "unplugged"
        var service = sp.GetRequiredService<ConsolidationService>();

        var plan = service.Plan(Options(dest), null, CancellationToken.None);
        Assert.Equal(1, plan.PendingFiles);
        Assert.NotEmpty(plan.Warnings);
        Assert.Single(plan.WaitingDisks);
        Assert.Throws<RecoveryServiceException>(() => service.Execute(Options(dest), null, CancellationToken.None));

        var result = service.Execute(Options(dest, allowPending: true), null, CancellationToken.None);
        Assert.Equal(1, result.Copied);
        Assert.Equal(1, result.WaitingForDisk);
    }

    [Theory]
    [InlineData("a:b?.txt", "a_b_.txt")]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("name. ", "name")]
    [InlineData("normal.jpg", "normal.jpg")]
    public void Linux_names_are_made_valid_for_Windows(string name, string expected) =>
        Assert.Equal(expected, ConsolidationPlanner.SafeName(name));
}
