using Microsoft.Extensions.DependencyInjection;
using RecoveryBLL.Dtos;
using RecoveryBLL.Models;
using RecoveryBLL.Services;

namespace RecoveryBLL.Tests;

/// <summary>Phase 3: the junk and keep-together rules (pure), and what the copy does with them.</summary>
public class FileTaggerTests
{
    static readonly JunkRules Rules = new()
    {
        SkinSizes = [(64, 64), (64, 32)],
        IconSizes = new HashSet<int> { 16, 32, 256 },
        ThumbnailSizes = [(160, 120)],
        ScrapBelow = new Dictionary<string, long> { ["Videos"] = 16384 },
        JunkFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "desktop.ini" },
        JunkFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".thumbnails" },
        AssetFolderMinImages = 3,
        AssetFolderMaxSide = 256,
    };

    static readonly BundleRule[] Bundles =
    [
        new("Minecraft", "server.properties", null, false, BundleAction.Keep),
        new("Minecraft", "level.dat", null, false, BundleAction.Keep),
        new("Code project", ".git", null, false, BundleAction.Keep),
        new("App data", null, "AppData", false, BundleAction.Review),
        new("Programs", null, null, true, BundleAction.Review),
    ];

    static long nextId;
    static FileFactsDto F(string path, long size = 1000, string? media = null, int? w = null, int? h = null, bool camera = false, long disk = 1) =>
        new(++nextId, disk, path, Path.GetExtension(path).TrimStart('.'), size, media, w, h, camera);

    static FileTag? TagOf(FileTagResult r, FileFactsDto f) => r.Tags.TryGetValue(f.Id, out var t) ? t : null;

    [Fact]
    public void Keep_together_folders_are_whole_and_the_outermost_folder_wins()
    {
        var props = F(@"backup\server\server.properties");
        var region = F(@"backup\server\world\region\r.0.0.mca", 5_000_000);
        var level = F(@"backup\server\world\level.dat");
        var texture = F(@"backup\server\mods\pack\icon.png", 900, "Photos", 64, 64);    // a skin size, but part of the server
        var outside = F(@"backup\notes.txt");
        var git = F(@"code\app\.git\HEAD");
        var src = F(@"code\app\src\main.cs");
        var rootMarker = F(@"level.dat", disk: 2);                                       // a marker in a disk root: not a bundle
        var other = F(@"photos\a.jpg", disk: 2);
        var r = FileTagger.Tag([props, region, level, texture, outside, git, src, rootMarker, other], Rules, Bundles);

        foreach (var f in new[] { props, region, level, texture })
            Assert.Equal(new FileTag(TagKind.KeepTogether, "Minecraft", @"backup\server"), TagOf(r, f));
        Assert.Null(TagOf(r, outside));
        Assert.Equal(@"code\app", TagOf(r, src)!.BundleRoot);
        Assert.Equal(TagKind.KeepTogether, TagOf(r, git)!.Kind);
        Assert.Null(TagOf(r, rootMarker));
        Assert.Null(TagOf(r, other));
        Assert.Equal(2, r.Bundles.Count);
        Assert.Equal(@"backup\server", r.Bundles[0].Root);                                  // largest first
        Assert.Equal(4, r.Bundles[0].Files);
    }

    [Fact]
    public void Programs_and_named_folders_are_reinstallable()
    {
        var exe = F(@"Games\Tool\tool.exe");
        var dlls = Enumerable.Range(0, 3).Select(i => F($@"Games\Tool\lib{i}.dll")).ToList();
        var lonelyExe = F(@"Downloads\setup.exe");                                        // an installer alone is not a program folder
        var appData = F(@"Users\me\AppData\Local\thing\cache.bin");
        var r = FileTagger.Tag([exe, .. dlls, lonelyExe, appData], Rules, Bundles);

        Assert.Equal(new FileTag(TagKind.ReviewTogether, "Programs", @"Games\Tool"), TagOf(r, exe));
        Assert.All(dlls, d => Assert.Equal(TagKind.ReviewTogether, TagOf(r, d)!.Kind));
        Assert.Null(TagOf(r, lonelyExe));
        Assert.Equal(new FileTag(TagKind.ReviewTogether, "App data", @"Users\me\AppData"), TagOf(r, appData));
    }

    [Fact]
    public void Junk_reasons_follow_the_rules_and_camera_data_protects_a_photo()
    {
        var ini = F(@"Pictures\desktop.ini", 100);
        var cached = F(@"home\.thumbnails\large\x.png", 5000, "Photos", 400, 300);
        var scrap = F(@"Videos\f1.mp4", 24, "Videos");
        var realVideo = F(@"Videos\holiday.mp4", 50_000_000, "Videos");
        var ico = F(@"Photos\app.ico", 4000, "Photos");
        var skin = F(@"Photos\steve.png", 1200, "Photos", 64, 32);
        var icon = F(@"Photos\logo.png", 3000, "Photos", 256, 256);
        var squareJpg = F(@"Photos\avatar.jpg", 3000, "Photos", 256, 256);              // square, but a JPEG: not an icon
        var thumb = F(@"Photos\t.jpg", 5000, "Photos", 160, 120);
        var cameraThumb = F(@"Photos\old camera.jpg", 5000, "Photos", 160, 120, camera: true);
        var set = Enumerable.Range(0, 3).Select(i => F($@"game\assets\tile{i}.png", 700, "Photos", 24, 24)).ToList();
        var notASet = Enumerable.Range(0, 2).Select(i => F($@"few\tile{i}.png", 700, "Photos", 24, 24)).ToList();
        var photo = F(@"Photos\beach.jpg", 3_000_000, "Photos", 4000, 3000, camera: true);
        var unmeasured = F(@"Photos\new.jpg", 3_000_000, "Photos");

        var all = new List<FileFactsDto> { ini, cached, scrap, realVideo, ico, skin, icon, squareJpg, thumb, cameraThumb, photo, unmeasured };
        all.AddRange(set);
        all.AddRange(notASet);
        var r = FileTagger.Tag(all, Rules, Bundles);

        string? Reason(FileFactsDto f) => TagOf(r, f)?.Reason;
        Assert.Equal(FileTagger.SystemFiles, Reason(ini));
        Assert.Equal(FileTagger.Caches, Reason(cached));
        Assert.Equal(FileTagger.Scraps, Reason(scrap));
        Assert.Null(Reason(realVideo));
        Assert.Equal(FileTagger.IconsAndSkins, Reason(ico));
        Assert.Equal(FileTagger.IconsAndSkins, Reason(skin));
        Assert.Equal(FileTagger.IconsAndSkins, Reason(icon));
        Assert.Null(Reason(squareJpg));
        Assert.Equal(FileTagger.Thumbnails, Reason(thumb));
        Assert.Null(Reason(cameraThumb));
        Assert.All(set, f => Assert.Equal(FileTagger.ImageSets, Reason(f)));
        Assert.All(notASet, f => Assert.Null(Reason(f)));
        Assert.Null(Reason(photo));
        Assert.Null(Reason(unmeasured));
        Assert.Equal(1, r.UnmeasuredImages);
        Assert.All(r.Tags.Values, t => Assert.Equal(TagKind.Junk, t.Kind));
        Assert.Equal(3, r.Examples[FileTagger.IconsAndSkins].Count);

        var off = FileTagger.Tag(all, Rules, Bundles, junk: false);
        Assert.Empty(off.Tags);
    }
}

public class JunkCopyTests
{
    static VolumeIdentity Vol(string serial, string root) =>
        new(serial, 1_000_000_000, "T" + serial, "NTFS", root.EndsWith('\\') ? root : root + "\\");

    static ConsolidateOptions Options(string dest, JunkHandling junk, long? max = null) =>
        new([new DestinationRequest(dest, max, Vol("DST", dest))], ReserveBytes: 0, Verify: true, WaitForUnplugged: false, Junk: junk);

    /// <summary>Two backups of the same server (identical files), a junk file, a scrap video, a photo with a junk copy.</summary>
    static (ServiceProvider Sp, string Dest) Setup(TempDir d)
    {
        var world = TestHost.Random(200_000, 1);
        var props = TestHost.Random(300, 2);
        foreach (var backup in new[] { "first", "second" })
        {
            d.File($@"src\backup\{backup}\server.properties", props);
            d.File($@"src\backup\{backup}\world\region\r.0.0.mca", world);
            d.File($@"src\backup\{backup}\textures\grass.png", TestHost.Random(5_000, 3));
        }
        d.File(@"src\Pictures\desktop.ini", TestHost.Random(120, 4));
        d.File(@"src\Videos\f123.mp4", TestHost.Random(24, 5));
        var beach = TestHost.Random(150_000, 6);
        d.File(@"src\Photos\beach.jpg", beach);
        d.File(@"src\cache\.thumbnails\beach.jpg", beach);                                // junk copy of a real photo
        var dest = Path.Combine(d.Path, "dest");
        Directory.CreateDirectory(dest);
        var sp = TestHost.Create(d.Path, new ListLog());
        sp.GetRequiredService<ScanService>().Scan(new ScanRequest(Path.Combine(d.Path, "src"), [], Vol("SRC", Path.Combine(d.Path, "src"))), null, CancellationToken.None);
        return (sp, dest);
    }

    [Fact]
    public void Keep_together_folders_are_copied_whole_and_junk_is_set_aside_after_your_own_files()
    {
        using var d = new TempDir();
        var (sp, dest) = Setup(d);
        using var _ = sp;
        var service = sp.GetRequiredService<ConsolidationService>();
        var plan = service.Plan(Options(dest, JunkHandling.SetAside), null, CancellationToken.None);
        PlanItem Item(string path) => plan.Items.Single(i => i.SourcePath.Equals(path, StringComparison.OrdinalIgnoreCase));

        // Both backups complete, at their own place, although their files are identical.
        foreach (var backup in new[] { "first", "second" })
        {
            var mca = Item($@"backup\{backup}\world\region\r.0.0.mca");
            Assert.Equal(CopyAction.KeepTogether, mca.Action);
            Assert.Equal($@"Apps and data\TSRC_SRC\backup\{backup}\world\region\r.0.0.mca", mca.DestPath);
            Assert.Equal(CopyAction.KeepTogether, Item($@"backup\{backup}\textures\grass.png").Action);   // not sorted into Photos
        }
        Assert.Equal(CopyAction.Review, Item(@"Pictures\desktop.ini").Action);
        Assert.Equal(@"_Review\System files\TSRC_SRC\Pictures\desktop.ini", Item(@"Pictures\desktop.ini").DestPath);
        Assert.Equal(CopyAction.Review, Item(@"Videos\f123.mp4").Action);
        Assert.Equal(CopyAction.Keep, Item(@"Photos\beach.jpg").Action);                                 // the real copy wins
        Assert.Equal(CopyAction.SkippedDuplicate, Item(@"cache\.thumbnails\beach.jpg").Action);          // junk extra copy: not copied
        Assert.Equal(2 * 3, plan.Files(CopyAction.KeepTogether));
        Assert.Equal(144, plan.ReviewBytes);

        var result = service.Execute(Options(dest, JunkHandling.SetAside), null, CancellationToken.None);
        Assert.Equal(0, result.Errors);
        Assert.True(File.Exists(Path.Combine(dest, @"Apps and data\TSRC_SRC\backup\second\server.properties")));
        Assert.True(File.Exists(Path.Combine(dest, @"_Review\Scraps\TSRC_SRC\Videos\f123.mp4")));
        Assert.True(File.Exists(Path.Combine(dest, @"Photos\beach.jpg")));

        var again = service.Plan(Options(dest, JunkHandling.SetAside), null, CancellationToken.None);
        Assert.Equal(0, again.FilesToCopy);                                                              // a rerun finds everything
    }

    [Fact]
    public void Junk_gets_only_the_space_that_is_left_and_can_be_skipped_or_switched_off()
    {
        using var d = new TempDir();
        var (sp, dest) = Setup(d);
        using var _ = sp;
        var service = sp.GetRequiredService<ConsolidationService>();

        // Room for everything except the junk: unique files and the servers first, junk does not fit, which is fine.
        // In 4 KB clusters: per server backup 4,096 + 200,704 + 8,192; the photo 151,552. The two junk files need 4,096 each.
        const long ownFiles = 2 * (4_096 + 200_704 + 8_192) + 151_552;
        var plan = service.Plan(Options(dest, JunkHandling.SetAside, max: ownFiles), null, CancellationToken.None);
        Assert.True(plan.UniqueFits);
        Assert.Equal(2, plan.ReviewNoRoom);
        Assert.Equal(0, plan.Files(CopyAction.Review));

        var skip = service.Plan(Options(dest, JunkHandling.Skip), null, CancellationToken.None);
        Assert.Equal(2, skip.Files(CopyAction.SkippedReview));
        Assert.Equal(0, skip.Files(CopyAction.Review));

        var off = service.Plan(Options(dest, JunkHandling.Off), null, CancellationToken.None);
        Assert.Equal(0, off.ReviewFiles);
        Assert.Equal(CopyAction.Keep, off.Items.Single(i => i.SourcePath == @"Videos\f123.mp4").Action);
        Assert.Equal(6, off.Files(CopyAction.KeepTogether));                                             // folders stay whole anyway
    }
}
