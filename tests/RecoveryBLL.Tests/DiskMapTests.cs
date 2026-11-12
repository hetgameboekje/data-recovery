using Microsoft.Extensions.DependencyInjection;
using RecoveryBLL.Dtos;
using RecoveryBLL.Models;
using RecoveryBLL.Services;

namespace RecoveryBLL.Tests;

public class TreemapLayoutTests
{
    static MapNode Folder(string name, params long[] files)
    {
        var f = new MapNode { Name = name, Kind = MapNodeKind.Folder };
        foreach (var (size, i) in files.Select((s, i) => (s, i)))
            f.Children.Add(new MapNode { Name = $"f{i}", Kind = MapNodeKind.File, Bytes = size, Parent = f });
        f.Bytes = files.Sum();
        return f;
    }

    [Fact]
    public void Blocks_fill_the_area_in_proportion_to_their_size_without_overlapping()
    {
        var root = Folder("root", 600, 300, 50, 25, 15, 6, 3, 1);
        var map = TreemapLayout.Layout(root, new MapRect(0, 0, 800, 500), minSide: 0.001);

        Assert.Equal(8, map.Leaves.Count);
        Assert.Equal(800 * 500, map.Leaves.Sum(l => l.Rect.Area), 3);
        foreach (var l in map.Leaves)
        {
            Assert.Equal(l.Node.Bytes / 1000.0 * 800 * 500, l.Rect.Area, 3);
            Assert.True(l.Rect.X >= -1e-9 && l.Rect.Y >= -1e-9 && l.Rect.X + l.Rect.W <= 800 + 1e-6 && l.Rect.Y + l.Rect.H <= 500 + 1e-6);
        }
        for (int i = 0; i < map.Leaves.Count; i++)
            for (int j = i + 1; j < map.Leaves.Count; j++)
            {
                var a = map.Leaves[i].Rect; var b = map.Leaves[j].Rect;
                double ox = Math.Min(a.X + a.W, b.X + b.W) - Math.Max(a.X, b.X), oy = Math.Min(a.Y + a.H, b.Y + b.H) - Math.Max(a.Y, b.Y);
                Assert.False(ox > 1e-6 && oy > 1e-6, $"{map.Leaves[i].Node} overlaps {map.Leaves[j].Node}");
            }
    }

    [Fact]
    public void Blocks_stay_close_to_square()
    {
        var root = Folder("root", Enumerable.Repeat(10L, 100).ToArray());
        var map = TreemapLayout.Layout(root, new MapRect(0, 0, 1000, 1000), minSide: 0.001);
        Assert.All(map.Leaves, l => Assert.True(Math.Max(l.Rect.W / l.Rect.H, l.Rect.H / l.Rect.W) < 2.5));
    }

    [Fact]
    public void A_folder_too_small_to_split_is_one_block_and_hit_testing_finds_the_deepest_block()
    {
        var big = Folder("big", 1_000_000);
        var tiny = Folder("tiny", 1, 1, 1);
        var root = new MapNode { Name = "root", Kind = MapNodeKind.Root, Bytes = big.Bytes + tiny.Bytes };
        root.Children.Add(big); root.Children.Add(tiny);

        var map = TreemapLayout.Layout(root, new MapRect(0, 0, 100, 100));
        Assert.Contains(map.Leaves, l => l.Node == tiny);                       // not split into its 3 files
        Assert.DoesNotContain(map.Leaves, l => l.Node.Parent == tiny);
        Assert.Equal(big.Children[0], map.HitTest(50, 50));
        Assert.Null(map.HitTest(150, 50));
    }
}

public class DiskMapTests
{
    [Fact]
    public void Map_has_folders_sizes_types_and_extra_copies_from_the_catalog() => Check(sp => sp);

    [Fact]
    public void Map_works_on_SQL_Server_too()
    {
        if (Environment.GetEnvironmentVariable("RT_TEST_SQLSERVER") != "1") return;
        using var d = new TempDir();
        TestHost.WithLocalDb(d, sp => Run(sp, d));
    }

    static void Check(Func<ServiceProvider, ServiceProvider> pick)
    {
        using var d = new TempDir();
        using var sp = TestHost.Create(d.Path, new ListLog());
        Run(pick(sp), d);
    }

    static void Run(ServiceProvider sp, TempDir d)
    {
        var song = TestHost.Random(300_000, 1);
        d.File(@"src\Music\song.mp3", song);
        d.File(@"src\Backup\song copy.mp3", song);              // same content: one of the two is an extra copy
        d.File(@"src\Photos\Holiday\a.jpg", TestHost.Random(200_000, 2));
        d.File(@"src\Photos\Holiday\b.JPG", TestHost.Random(100_000, 3));
        d.File(@"src\notes.txt", TestHost.Random(5_000, 4));
        var scan = sp.GetRequiredService<ScanService>().Scan(new ScanRequest(Path.Combine(d.Path, "src"), []), null, CancellationToken.None);
        sp.GetRequiredService<FingerprintService>().Run(new FingerprintOptions(), null, CancellationToken.None, new HashSet<long> { scan.DiskId });

        var map = sp.GetRequiredService<DiskMapService>().Build(scan.DiskId, CancellationToken.None);

        var root = map.Root;
        Assert.Equal(MapNodeKind.Disk, root.Kind);
        Assert.Equal(905_000, root.Bytes);
        Assert.Equal(5, root.Files);
        Assert.Equal(300_000, root.ExtraCopyBytes);
        Assert.Equal(0, root.PendingBytes);

        MapNode Find(string relative) => Descendants(root).Single(n => n.RelativePath.EndsWith(relative, StringComparison.OrdinalIgnoreCase));
        var holiday = Find(@"Photos\Holiday");
        Assert.Equal(MapNodeKind.Folder, holiday.Kind);
        Assert.Equal(300_000, holiday.Bytes);
        Assert.Equal("a.jpg", holiday.Children[0].Name);                    // largest first
        Assert.Equal("Photos", map.Categories[holiday.Category].Name);
        Assert.Equal("#3B82C4", map.Categories[holiday.Category].Color);   // from dictionaries.xml
        Assert.Equal("Audio", map.Categories[Find(@"Music\song.mp3").Category].Name);

        var states = new[] { Find(@"Music\song.mp3").State, Find(@"Backup\song copy.mp3").State };
        Assert.Contains(MapFileState.Kept, states);
        Assert.Contains(MapFileState.ExtraCopy, states);

        var jpg = map.Extensions.Single(e => e.Ext == "jpg");                // case folded: a.jpg + b.JPG
        Assert.Equal(2, jpg.Files);
        Assert.Equal(map.Extensions.Max(e => e.Bytes), map.Extensions[0].Bytes);
        Assert.Equal(600_000, map.Categories.Single(c => c.Name == "Audio").Bytes);
        Assert.Equal(300_000, map.Categories.Single(c => c.Name == "Audio").ExtraCopyBytes);

        var all = sp.GetRequiredService<DiskMapService>().Build(null, CancellationToken.None);
        Assert.Equal(MapNodeKind.Root, all.Root.Kind);
        Assert.Single(all.Root.Children);
        Assert.Equal(905_000, all.Root.Bytes);
    }

    [Fact]
    public void Tiny_files_are_merged_into_one_block_per_folder()
    {
        using var d = new TempDir();
        d.File(@"src\big.bin", TestHost.Random(DiskMapService.MaxBlocks * 4, 1));
        for (int i = 0; i < 5; i++) d.File($@"src\small\{i}.txt", TestHost.Random(2, 10 + i));   // far below 1/MaxBlocks of the map
        using var sp = TestHost.Create(d.Path, new ListLog());
        var scan = sp.GetRequiredService<ScanService>().Scan(new ScanRequest(Path.Combine(d.Path, "src"), []), null, CancellationToken.None);

        var map = sp.GetRequiredService<DiskMapService>().Build(scan.DiskId, CancellationToken.None);
        var small = Descendants(map.Root).Single(n => n.Name == "small");
        var merged = Assert.Single(small.Children);
        Assert.Equal(MapNodeKind.SmallFiles, merged.Kind);
        Assert.Equal(5, merged.Files);
        Assert.Equal(10, merged.Bytes);
        Assert.Equal(@"small", merged.RelativePath[^5..]);                  // a merged block points at its folder
    }

    static IEnumerable<MapNode> Descendants(MapNode n)
    {
        foreach (var c in n.Children)
        {
            yield return c;
            foreach (var x in Descendants(c)) yield return x;
        }
    }
}
