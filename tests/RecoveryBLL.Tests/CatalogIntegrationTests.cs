using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using RecoveryBLL.Exceptions;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;
using RecoveryBLL.Services;

namespace RecoveryBLL.Tests;

/// <summary>The whole pipeline (scan -> funnel -> report -> merge) through the real BLL + DAL.</summary>
public class CatalogIntegrationTests
{
    static void MakeTestFiles(TempDir d)
    {
        var big = TestHost.Random(300_000, 10);
        d.File(@"src\a\video.mp4", big);
        d.File(@"src\b\video copy.mp4", big);                // duplicate (large)
        var edited = (byte[])big.Clone(); edited[150_000] ^= 1;
        d.File(@"src\b\video-edited.mp4", edited);            // same size + head/tail, different content
        var small = TestHost.Random(5000, 11);
        d.File(@"src\a\doc.txt", small);
        d.File(@"src\b\doc.txt", small);                      // duplicate (small)
        d.File(@"src\b\other.txt", TestHost.Random(5000, 12)); // same size, different
        d.File(@"src\a\unique.bin", TestHost.Random(7777, 13));
        d.File(@"src\a\empty.dat", []);
        d.File(@"src\a\IMG_0001.JPG", ImageHeaderReaderTests.BuildJpeg());
        d.File(@"src\a\copy.rtpart", [1, 2, 3]);               // partial copy: never catalogued
        d.File(@"src\_RecoveryToolkit\log.txt", [1]);         // toolkit folder: skipped
    }

    [Fact]
    public void Scan_fingerprint_and_report_classify_correctly()
    {
        using var d = new TempDir();
        MakeTestFiles(d);
        var log = new ListLog();
        using var sp = TestHost.Create(d.Path, log);

        var scan = sp.GetRequiredService<ScanService>().Scan(new ScanRequest(Path.Combine(d.Path, "src"), []), null, CancellationToken.None);
        Assert.True(scan.Completed);
        Assert.Equal(9, scan.Files);                          // .rtpart and _RecoveryToolkit excluded

        var stages = sp.GetRequiredService<FingerprintService>().Run(new FingerprintOptions(), null, CancellationToken.None,
            new HashSet<long> { scan.DiskId });
        // Quick hash only for files that share a size: 3 videos + 3 txt (unique.bin, the JPEG and empty.dat are never read)
        Assert.Equal(6, stages.Single(s => s.Stage == "Quick hash").Files);
        // Full hash only for large files whose quick hash collides: the 3 videos (txt files got it from the quick read)
        Assert.Equal(3, stages.Single(s => s.Stage == "Full hash").Files);

        var report = sp.GetRequiredService<ReportService>().GetReport();
        var all = report.Types.Sum(t => t.TotalBytes);
        Assert.Equal(0, report.PendingBytes);
        Assert.Equal(300_000 + 5000, report.RedundantBytes);  // one extra video + one extra doc.txt
        Assert.Equal(all - report.RedundantBytes, report.UniqueBytes);
        Assert.Equal(1, report.Disks.Single(x => x.Disk.Id == scan.DiskId).EmptyFiles);
    }

    [Fact]
    public void Rescan_keeps_fingerprints_and_detects_changes_and_deletions()
    {
        using var d = new TempDir();
        MakeTestFiles(d);
        using var sp = TestHost.Create(d.Path, new ListLog());
        var scan = sp.GetRequiredService<ScanService>();
        var hash = sp.GetRequiredService<FingerprintService>();
        var src = Path.Combine(d.Path, "src");

        var first = scan.Scan(new ScanRequest(src, []), null, CancellationToken.None);
        var only = new HashSet<long> { first.DiskId };
        hash.Run(new FingerprintOptions(), null, CancellationToken.None, only);

        scan.Scan(new ScanRequest(src, []), null, CancellationToken.None);
        Assert.All(hash.Run(new FingerprintOptions(), null, CancellationToken.None, only), s => Assert.Equal(0, s.Files));

        File.WriteAllBytes(Path.Combine(src, @"b\doc.txt"), TestHost.Random(5000, 99));   // changed
        File.Delete(Path.Combine(src, @"b\video copy.mp4"));                              // deleted
        var third = scan.Scan(new ScanRequest(src, []), null, CancellationToken.None);
        Assert.Equal(1, third.MarkedMissing);
        var again = hash.Run(new FingerprintOptions(), null, CancellationToken.None, only);
        Assert.Equal(1, again.Single(s => s.Stage == "Quick hash").Files);              // only the changed file

        var report = sp.GetRequiredService<ReportService>().GetReport();
        Assert.Equal(0, report.RedundantBytes);               // no duplicates left
    }

    [Fact]
    public void Merge_combines_two_catalogs_and_refuses_itself()
    {
        using var d = new TempDir();
        MakeTestFiles(d);
        var otherDir = Path.Combine(d.Path, "other");
        Directory.CreateDirectory(otherDir);
        using (var other = TestHost.Create(otherDir, new ListLog()))
        {
            var r = other.GetRequiredService<ScanService>().Scan(new ScanRequest(Path.Combine(d.Path, "src"), []), null, CancellationToken.None);
            other.GetRequiredService<FingerprintService>().Run(new FingerprintOptions(), null, CancellationToken.None, new HashSet<long> { r.DiskId });
        }

        using var sp = TestHost.Create(d.Path, new ListLog());
        var merge = sp.GetRequiredService<MergeService>().Merge(Path.Combine(otherDir, "catalog.db"), null, CancellationToken.None);
        Assert.Equal(9, merge.Files);
        Assert.Equal(300_000 + 5000, sp.GetRequiredService<ReportService>().GetReport().RedundantBytes);

        Assert.Throws<RecoveryServiceException>(() =>
            sp.GetRequiredService<MergeService>().Merge(Path.Combine(d.Path, "catalog.db"), null, CancellationToken.None));
    }

    [Fact]
    public void Scan_of_missing_folder_or_network_path_is_refused()
    {
        using var d = new TempDir();
        using var sp = TestHost.Create(d.Path, new ListLog());
        var scan = sp.GetRequiredService<ScanService>();
        Assert.Throws<RecoveryServiceException>(() => scan.Scan(new ScanRequest(Path.Combine(d.Path, "nope"), []), null, CancellationToken.None));
        Assert.Throws<RecoveryServiceException>(() => scan.Scan(new ScanRequest(@"\\server\share", []), null, CancellationToken.None));
    }

    /// <summary>Same pipeline on SQL Server LocalDB. Runs only when RT_TEST_SQLSERVER is set (needs LocalDB).</summary>
    [Fact]
    public void Sql_server_provider_gives_the_same_result()
    {
        if (Environment.GetEnvironmentVariable("RT_TEST_SQLSERVER") is not { Length: > 0 }) return;
        var dbName = "RtTest_" + Guid.NewGuid().ToString("N")[..8];
        var cs = $@"Server=(localdb)\MSSQLLocalDB;Database={dbName};Trusted_Connection=True;TrustServerCertificate=True";
        using var d = new TempDir();
        MakeTestFiles(d);
        try
        {
            using var sp = TestHost.Create(d.Path, new ListLog(), new AppSettings
            {
                Provider = DatabaseProvider.SqlServer, ConnectionString = cs, SafeConnectionDescription = "test",
                DictionariesFile = TestHost.DictionariesFile, LogDirectory = d.Path,
            });
            var r = sp.GetRequiredService<ScanService>().Scan(new ScanRequest(Path.Combine(d.Path, "src"), []), null, CancellationToken.None);
            sp.GetRequiredService<FingerprintService>().Run(new FingerprintOptions(), null, CancellationToken.None, new HashSet<long> { r.DiskId });
            var report = sp.GetRequiredService<ReportService>().GetReport();
            Assert.Equal(300_000 + 5000, report.RedundantBytes);
            Assert.Equal(0, report.PendingBytes);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            using var c = new SqlConnection(@"Server=(localdb)\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True");
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"IF DB_ID('{dbName}') IS NOT NULL BEGIN ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}]; END";
            cmd.ExecuteNonQuery();
        }
    }
}

public class DiskServiceTests
{
    sealed class FakeDevices(PhysicalDevice? d) : IPhysicalDeviceProvider
    {
        public PhysicalDevice? GetDeviceForVolume(string root) => d;
        public IReadOnlyList<PhysicalDevice> GetAll() => d == null ? [] : [d];
    }

    [Theory]
    [InlineData(BusType.Nvme, false, 8)]   // NVMe SSD: many readers
    [InlineData(BusType.Usb, false, 2)]    // USB SSD
    [InlineData(BusType.Sata, true, 1)]    // spinning HDD: always 1
    [InlineData(BusType.Usb, null, 1)]     // bridge hides the type: be safe
    public void Recommended_readers_follow_bus_and_media(BusType bus, bool? seekPenalty, int expected)
    {
        using var d = new TempDir();
        using var sp = TestHost.Create(d.Path, new ListLog());
        var svc = new DiskService(sp.GetRequiredService<IDiskRepository>(), sp.GetRequiredService<IVolumeProvider>(),
            new FakeDevices(null), sp.GetRequiredService<DictionaryService>());
        Assert.Equal(expected, svc.RecommendedThreads(new PhysicalDevice(1, "x", null, bus, seekPenalty, null, false, 0)));
        Assert.Equal(1, svc.RecommendedThreads(null));
    }
}
