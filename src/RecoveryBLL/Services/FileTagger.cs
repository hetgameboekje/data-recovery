using RecoveryBLL.Dtos;
using RecoveryBLL.Models;

namespace RecoveryBLL.Services;

/// <summary>
/// Phase 3 rules, from catalog facts only (pure: testable, no disk access). First the keep-together folders
/// (a file inside one is never junk), then the junk rules in this order:
///   System files     Thumbs.db, desktop.ini, ... (by name)
///   Caches           files inside a cache/thumbnail folder (by folder name)
///   Scraps           a "picture", "video" or "song" too small to be one (recovery leftovers)
///   Icons and skins  .ico files, PNG game skins (64×64, 64×32), small square icon sizes without camera data
///   Thumbnails       known thumbnail sizes without camera data
///   Image sets       a folder with hundreds of small images of one size without camera data
/// Nothing here deletes or moves anything: the copy puts tagged files aside.
/// </summary>
public static class FileTagger
{
    public const int ExamplesPerReason = 200;
    public const string SystemFiles = "System files", Caches = "Caches", Scraps = "Scraps",
                        IconsAndSkins = "Icons and skins", Thumbnails = "Thumbnails", ImageSets = "Image sets";

    static readonly HashSet<string> IconExtensions = new(StringComparer.OrdinalIgnoreCase) { "png", "gif", "bmp", "ico" };

    public static FileTagResult Tag(IReadOnlyList<FileFactsDto> files, JunkRules rules, IReadOnlyList<BundleRule> bundles, bool junk = true)
    {
        var tags = new Dictionary<long, FileTag>();
        var roots = FindBundleRoots(files, bundles);

        // ---- keep-together folders: the outermost root above a file wins
        var bundleStats = new Dictionary<(long Disk, string Root), (BundleRule Rule, long Files, long Bytes)>();
        foreach (var f in files)
        {
            if (!roots.TryGetValue(f.DiskId, out var diskRoots)) continue;
            var root = OutermostRootOf(f.Path, diskRoots);
            if (root == null) continue;
            var rule = diskRoots[root];
            tags[f.Id] = new FileTag(rule.Action == BundleAction.Keep ? TagKind.KeepTogether : TagKind.ReviewTogether, rule.Name, root);
            var s = bundleStats.TryGetValue((f.DiskId, root), out var x) ? x : (rule, 0L, 0L);
            bundleStats[(f.DiskId, root)] = (rule, s.Item2 + 1, s.Item3 + f.Size);
        }

        long unmeasured = 0;
        if (junk)
        {
            // Image sets: many small images of one size in one folder, without camera data.
            var setCounts = new Dictionary<(long, string, int, int), int>();
            foreach (var f in files)
                if (IsSmallPlainImage(f, rules.AssetFolderMaxSide) && !tags.ContainsKey(f.Id))
                {
                    var k = (f.DiskId, Folder(f.Path), f.Width!.Value, f.Height!.Value);
                    setCounts[k] = setCounts.GetValueOrDefault(k) + 1;
                }

            foreach (var f in files)
            {
                if (tags.ContainsKey(f.Id)) continue;
                if (f.MediaType == "Photos" && f.Width == null && IsProbeable(f)) unmeasured++;
                var reason = JunkReason(f, rules, setCounts);
                if (reason != null) tags[f.Id] = new FileTag(TagKind.Junk, reason);
            }
        }

        var byId = files.ToDictionary(f => f.Id);
        var groups = tags.GroupBy(t => (t.Value.Kind, t.Value.Reason))
            .Select(g => new TagGroup(g.Key.Kind, g.Key.Reason, g.Count(), g.Sum(t => byId[t.Key].Size)))
            .OrderByDescending(g => g.Bytes).ToList();
        var examples = tags.Where(t => t.Value.Kind == TagKind.Junk)
            .GroupBy(t => t.Value.Reason)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<TaggedFile>)g.Select(t => byId[t.Key])
                .OrderByDescending(f => f.Size).Take(ExamplesPerReason)
                .Select(f => new TaggedFile(f.DiskId, f.Path, f.Size)).ToList());
        var bundleList = bundleStats
            .Select(kv => new BundleSummary(kv.Key.Disk, kv.Key.Root, kv.Value.Rule.Name,
                kv.Value.Rule.Action == BundleAction.Keep ? TagKind.KeepTogether : TagKind.ReviewTogether, kv.Value.Files, kv.Value.Bytes))
            .OrderByDescending(b => b.Bytes).ToList();

        return new FileTagResult { Tags = tags, Groups = groups, Bundles = bundleList, Examples = examples, UnmeasuredImages = unmeasured };
    }

    static string? JunkReason(FileFactsDto f, JunkRules rules, Dictionary<(long, string, int, int), int> setCounts)
    {
        var name = FileName(f.Path);
        if (rules.JunkFiles.Contains(name)) return SystemFiles;
        var segments = f.Path.Split('\\');
        for (int i = 0; i < segments.Length - 1; i++)
            if (rules.JunkFolders.Contains(segments[i])) return Caches;
        if (f.MediaType != null && rules.ScrapBelow.TryGetValue(f.MediaType, out var below) && f.Size < below) return Scraps;

        var ext = (f.Ext ?? "").TrimStart('.');
        if (ext.Equals("ico", StringComparison.OrdinalIgnoreCase)) return IconsAndSkins;
        if (f.Width is not { } w || f.Height is not { } h || f.HasCamera) return null;
        if (ext.Equals("png", StringComparison.OrdinalIgnoreCase) && rules.SkinSizes.Contains((w, h))) return IconsAndSkins;
        if (w == h && rules.IconSizes.Contains(w) && IconExtensions.Contains(ext)) return IconsAndSkins;
        if (rules.ThumbnailSizes.Contains((w, h))) return Thumbnails;
        if (IsSmallPlainImage(f, rules.AssetFolderMaxSide)
            && setCounts.GetValueOrDefault((f.DiskId, Folder(f.Path), w, h)) >= rules.AssetFolderMinImages) return ImageSets;
        return null;
    }

    static bool IsSmallPlainImage(FileFactsDto f, int maxSide) =>
        f.Width is { } w && f.Height is { } h && !f.HasCamera && Math.Max(w, h) <= maxSide;

    static bool IsProbeable(FileFactsDto f) => (f.Ext ?? "").ToLowerInvariant() is "jpg" or "jpeg" or "jpe" or "jfif" or "png" or "gif" or "bmp" or "webp";

    /// <summary>Per disk: bundle root folder → the rule that made it one (only outermost roots are kept).</summary>
    internal static Dictionary<long, Dictionary<string, BundleRule>> FindBundleRoots(IReadOnlyList<FileFactsDto> files, IReadOnlyList<BundleRule> rules)
    {
        var found = new Dictionary<long, Dictionary<string, BundleRule>>();
        void Add(long disk, string root, BundleRule rule)
        {
            if (root.Length == 0) return;   // never a whole disk
            var d = found.TryGetValue(disk, out var x) ? x : found[disk] = new(StringComparer.OrdinalIgnoreCase);
            d.TryAdd(root, rule);
        }

        var markers = rules.Where(r => r.Marker != null).ToList();
        var folders = rules.Where(r => r.Folder != null).ToList();
        var program = rules.FirstOrDefault(r => r.Program);
        var exeDll = new Dictionary<(long, string), (int Exe, int Dll)>();

        foreach (var f in files)
        {
            var segments = f.Path.Split('\\');
            for (int i = 0; i < segments.Length; i++)
            {
                bool last = i == segments.Length - 1;
                foreach (var r in markers)
                    if (segments[i].Equals(r.Marker, StringComparison.OrdinalIgnoreCase))
                        Add(f.DiskId, string.Join('\\', segments, 0, i), r);          // the folder that holds the marker
                if (!last)
                    foreach (var r in folders)
                        if (segments[i].Equals(r.Folder, StringComparison.OrdinalIgnoreCase))
                            Add(f.DiskId, string.Join('\\', segments, 0, i + 1), r);  // the folder itself
            }
            if (program != null)
            {
                var ext = (f.Ext ?? "").TrimStart('.').ToLowerInvariant();
                if (ext is "exe" or "dll")
                {
                    var k = (f.DiskId, Folder(f.Path));
                    var c = exeDll.GetValueOrDefault(k);
                    exeDll[k] = ext == "exe" ? (c.Exe + 1, c.Dll) : (c.Exe, c.Dll + 1);
                }
            }
        }
        if (program != null)
            foreach (var ((disk, folder), c) in exeDll)
                if (c.Exe >= 1 && c.Dll >= 3) Add(disk, folder, program);

        // Keep only the outermost root: a world inside a server folder belongs to the server.
        foreach (var disk in found.Keys.ToList())
        {
            var outer = new Dictionary<string, BundleRule>(StringComparer.OrdinalIgnoreCase);
            foreach (var (root, rule) in found[disk].OrderBy(kv => kv.Key.Length))
                if (OutermostRootOf(root + "\\x", outer) == null) outer[root] = rule;
            found[disk] = outer;
        }
        return found;
    }

    /// <summary>The bundle root that contains <paramref name="path"/> (roots are outermost only, so at most one matches).</summary>
    static string? OutermostRootOf(string path, Dictionary<string, BundleRule> roots)
    {
        for (int i = path.IndexOf('\\'); i > 0; i = path.IndexOf('\\', i + 1))
            if (roots.ContainsKey(path[..i])) return path[..i];
        return null;
    }

    static string Folder(string path) => path.LastIndexOf('\\') is var i and > 0 ? path[..i] : "";
    static string FileName(string path) => path[(path.LastIndexOf('\\') + 1)..];
}
