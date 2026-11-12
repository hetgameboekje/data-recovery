using RecoveryBLL.Dtos;
using RecoveryBLL.Models;

namespace RecoveryBLL.Services;

/// <summary>
/// The copy plan, computed from the catalog alone (no disk access, so it is testable and repeatable).
/// Every file on a non-destination disk gets one label:
///   1. Keep          - the best copy of its content goes to the main folders: &lt;Category&gt;\&lt;original path&gt;
///   2. Duplicate     - identical to a kept file: _Duplicates\&lt;disk&gt;\&lt;original path&gt; (or skipped when switched off)
///      OlderVersion  - same folder + name as a kept file but different content: the newest is kept,
///                      older ones go to _OlderVersions\&lt;disk&gt;\&lt;original path&gt;
///   3. AlreadyThere  - the content is on a destination disk already
///   Phase 3 tags (FileTagger) come first:
///      KeepTogether  - inside a keep-together folder: Apps and data\&lt;disk&gt;\&lt;original path&gt;, whole, never deduplicated
///      Review        - junk or a reinstallable folder: _Review\&lt;reason&gt;\&lt;disk&gt;\&lt;original path&gt;, after unique files
///                      (SkippedReview when switched off). Junk copies of a kept file are not copied again.
/// Space: unique files (Keep + OlderVersion) first, on the destinations in the given order (main, then overflow).
/// Duplicates only get what is left. If a unique file does not fit anywhere, old _Duplicates files on a
/// destination are evicted (oldest first) - but only those whose content is also kept outside _Duplicates.
/// A unique file is never removed to make room. Running the planner again after a partial copy is safe:
/// copied files are in the catalog with their hash, so they come back as AlreadyThere.
/// </summary>
internal static class ConsolidationPlanner
{
    public const string OlderVersionsFolder = "_OlderVersions";
    public const string DuplicatesFolder = "_Duplicates";
    public const string AppsFolder = "Apps and data";
    public const string ReviewFolder = "_Review";
    public const long Fat32MaxFileSize = uint.MaxValue;   // 4 GB - 1 byte
    const long Cluster = 4096;

    public sealed record Result(List<PlanItem> Items, List<PlanEviction> Evictions, long PendingFiles, long PendingBytes);

    public static Result Build(IReadOnlyList<PlanFileDto> files, IReadOnlyList<DestinationPlan> destinations,
                               IReadOnlyDictionary<long, string> diskFolders, bool copyDuplicates,
                               IReadOnlyDictionary<long, FileTag>? tags = null, JunkHandling junk = JunkHandling.SetAside)
    {
        tags ??= new Dictionary<long, FileTag>();
        FileTag? TagOf(PlanFileDto f)
        {
            if (!tags.TryGetValue(f.Id, out var t)) return null;
            if (junk != JunkHandling.Off) return t;
            // Off: no junk; reinstallable folders are still kept whole, but like your own files.
            return t.Kind switch { TagKind.Junk => null, TagKind.ReviewTogether => t with { Kind = TagKind.KeepTogether }, _ => t };
        }
        var destById = destinations.ToDictionary(d => d.DiskId);
        var onDest = files.Where(f => destById.ContainsKey(f.DiskId)).ToList();
        var sources = files.Where(f => !destById.ContainsKey(f.DiskId)).ToList();

        // ---- what the destinations already hold (paths are relative to the destination folder, one shared namespace)
        var presentKeys = onDest.Where(f => f.FullHash != null).Select(Key).ToHashSet();
        // Keep-together files are "already there" only at their own place (the folder must be complete).
        var presentAt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingMtime = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var keptOutsideDuplicates = new Dictionary<string, PlanFileDto>();
        foreach (var f in onDest)
        {
            var rel = RelativeTo(f.Path, destById[f.DiskId].Prefix);
            if (rel == null) continue;
            taken.Add(rel);
            existingMtime[rel] = f.MtimeTicks;
            if (f.FullHash != null) presentAt.Add(rel + "|" + Key(f));
            if (f.FullHash != null && !IsUnder(rel, DuplicatesFolder)) keptOutsideDuplicates.TryAdd(Key(f), f);
        }
        var evictable = destinations.ToDictionary(d => d.DiskId, d => new Queue<PlanFileDto>(onDest
            .Where(f => f.DiskId == d.DiskId && f.FullHash != null && keptOutsideDuplicates.ContainsKey(Key(f))
                        && RelativeTo(f.Path, d.Prefix) is { } rel && IsUnder(rel, DuplicatesFolder))
            .OrderBy(f => f.MtimeTicks).ThenBy(f => f.Id)));
        var evictableBytes = evictable.ToDictionary(kv => kv.Key, kv => kv.Value.Sum(f => Alloc(f.Size)));

        // ---- label every source file
        var items = new List<PlanItem>(sources.Count);
        var keepers = new List<PlanItem>();
        var duplicates = new List<PlanItem>();
        var together = new List<(PlanItem Item, FileTag Tag)>();
        var review = new List<(PlanItem Item, FileTag Tag)>();
        PlanItem NewItem(PlanFileDto f) => new()
        {
            FileId = f.Id, SourceDiskId = f.DiskId, SourcePath = f.Path, Size = f.Size, MtimeTicks = f.MtimeTicks,
            Category = string.IsNullOrEmpty(f.MediaType) ? "Other" : f.MediaType, FullHash = f.FullHash,
        };

        // ---- keep-together folders: every file, at its own place (a server or database only works complete)
        var loose = new List<PlanFileDto>(sources.Count);
        foreach (var f in sources)
        {
            if (TagOf(f) is not { Kind: TagKind.KeepTogether or TagKind.ReviewTogether } t) { loose.Add(f); continue; }
            var item = NewItem(f);
            items.Add(item);
            if (t.Kind == TagKind.KeepTogether) together.Add((item, t));
            else if (junk == JunkHandling.Skip) item.Action = CopyAction.SkippedReview;
            else review.Add((item, t));
        }

        foreach (var group in loose.GroupBy(Key))
        {
            bool there = presentKeys.Contains(group.Key);
            // The copy that is kept: not junk if any copy is not junk, then the oldest modified time (closest to the
            // original), then the shortest path.
            var ordered = group.OrderBy(f => TagOf(f) == null ? 0 : 1).ThenBy(f => f.MtimeTicks).ThenBy(f => f.Path.Length).ThenBy(f => f.Id).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                var f = ordered[i];
                var item = NewItem(f);
                var tag = TagOf(f);
                if (there) item.Action = CopyAction.AlreadyThere;
                else if (i == 0 && tag != null)
                {
                    if (junk == JunkHandling.Skip) item.Action = CopyAction.SkippedReview;
                    else review.Add((item, tag));
                }
                else if (i == 0) keepers.Add(item);
                else if (tag != null) item.Action = CopyAction.SkippedDuplicate;   // an extra copy of junk is not worth space
                else if (copyDuplicates) { item.Action = CopyAction.Duplicate; duplicates.Add(item); }
                else item.Action = CopyAction.SkippedDuplicate;
                items.Add(item);
            }
        }

        string Claim(string rel)
        {
            var dir = Path.GetDirectoryName(rel) ?? "";
            var name = Path.GetFileNameWithoutExtension(rel);
            var ext = Path.GetExtension(rel);
            var candidate = rel;
            for (int n = 2; taken.Contains(candidate); n++) candidate = Path.Combine(dir, $"{name} ({n}){ext}");
            taken.Add(candidate);
            return candidate;
        }
        string DiskFolder(long diskId) => diskFolders.TryGetValue(diskId, out var n) ? n : "Disk" + diskId;

        // ---- same folder + name, different content: newest wins the main path
        foreach (var group in keepers.GroupBy(MainPath, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group.OrderByDescending(k => k.MtimeTicks).ThenBy(k => k.FileId).ToList();
            int firstOlder = 1;
            var newest = ordered[0];
            newest.Action = CopyAction.Keep;
            if (existingMtime.TryGetValue(group.Key, out var existing))
            {
                if (newest.MtimeTicks > existing)
                {
                    newest.DestPath = Claim(group.Key);
                    newest.Note = $"newer than the file already at {group.Key}";
                }
                else firstOlder = 0;   // the destination already holds the newest version
            }
            else newest.DestPath = Claim(group.Key);

            for (int i = firstOlder; i < ordered.Count; i++)
            {
                ordered[i].Action = CopyAction.OlderVersion;
                ordered[i].DestPath = Claim(Path.Combine(OlderVersionsFolder, DiskFolder(ordered[i].SourceDiskId), SafeRelative(ordered[i].SourcePath)));
            }
        }
        foreach (var d in duplicates)
            d.DestPath = Claim(Path.Combine(DuplicatesFolder, DiskFolder(d.SourceDiskId), SafeRelative(d.SourcePath)));
        foreach (var (item, tag) in together)
            PlaceAt(item, Path.Combine(AppsFolder, DiskFolder(item.SourceDiskId), SafeRelative(item.SourcePath)), CopyAction.KeepTogether);
        foreach (var (item, tag) in review)
            PlaceAt(item, Path.Combine(ReviewFolder, SafeName(tag.Reason), DiskFolder(item.SourceDiskId), SafeRelative(item.SourcePath)), CopyAction.Review);

        void PlaceAt(PlanItem item, string rel, CopyAction action)
        {
            if (item.FullHash != null && presentAt.Contains(rel + "|" + Convert.ToHexString(item.FullHash)))
            {
                item.Action = CopyAction.AlreadyThere;
                return;
            }
            item.Action = action;
            item.DestPath = Claim(rel);
        }

        // ---- space: unique content first (may evict old duplicates), then duplicates in what is left
        var remaining = destinations.ToDictionary(d => d.DiskId, d => Math.Max(0, d.UsableBytes));
        var evictions = new List<PlanEviction>();
        static IEnumerable<PlanItem> InOrder(IEnumerable<PlanItem> list) =>
            list.OrderBy(i => i.SourceDiskId).ThenBy(i => i.SourcePath, StringComparer.OrdinalIgnoreCase);
        foreach (var item in InOrder(keepers.Concat(together.Select(t => t.Item)).Where(i => i.Action != CopyAction.AlreadyThere)))
            Place(item, allowEviction: true);
        foreach (var item in InOrder(review.Select(r => r.Item).Where(i => i.Action == CopyAction.Review)))
            Place(item, allowEviction: false);
        foreach (var item in InOrder(duplicates))
            Place(item, allowEviction: false);

        long pendingFiles = 0, pendingBytes = 0;
        foreach (var f in files.Where(f => f.Kind == PlanFileKind.Pending)) { pendingFiles++; pendingBytes += f.Size; }
        return new Result(items, evictions, pendingFiles, pendingBytes);

        void Place(PlanItem item, bool allowEviction)
        {
            long need = Alloc(item.Size);
            var candidates = destinations.Where(d => !d.IsFat || item.Size <= Fat32MaxFileSize).ToList();
            var target = candidates.FirstOrDefault(d => remaining[d.DiskId] >= need);
            if (target == null && allowEviction)
            {
                target = candidates.FirstOrDefault(d => remaining[d.DiskId] + evictableBytes[d.DiskId] >= need);
                if (target != null)
                {
                    var queue = evictable[target.DiskId];
                    while (remaining[target.DiskId] < need)
                    {
                        var e = queue.Dequeue();
                        var freed = Alloc(e.Size);
                        evictableBytes[target.DiskId] -= freed;
                        remaining[target.DiskId] += freed;
                        target.EvictedBytes += e.Size;
                        var kept = keptOutsideDuplicates[Key(e)];
                        evictions.Add(new PlanEviction(e.Id, e.DiskId, e.Path, e.Size, kept.DiskId, kept.Path));
                    }
                }
            }
            if (target == null)
            {
                item.Note = item.Action == CopyAction.Duplicate ? PlanNotes.DuplicateNoRoom
                    : item.Action == CopyAction.Review ? PlanNotes.ReviewNoRoom
                    : candidates.Count == 0 ? "larger than 4 GB: FAT32 destinations cannot hold it"
                    : "no room on any destination";
                item.Action = CopyAction.NoRoom;
                item.DestDiskId = null;
                return;
            }
            item.DestDiskId = target.DiskId;
            item.DestPath = target.Prefix.Length == 0 ? item.DestPath : Path.Combine(target.Prefix, item.DestPath!);
            remaining[target.DiskId] -= need;
            target.PlannedBytes += item.Size;
            target.PlannedFiles++;
        }
    }

    /// <summary>Content identity: the full hash, or the file itself when it was never hashed (unique size or still pending).</summary>
    static string Key(PlanFileDto f) => f.FullHash != null ? Convert.ToHexString(f.FullHash) : "#" + f.Id;

    internal static long Alloc(long size) => (size + Cluster - 1) / Cluster * Cluster;

    /// <summary>&lt;Category&gt;\&lt;original path&gt;, without repeating the category when the path already starts with it.</summary>
    static string MainPath(PlanItem item)
    {
        var rel = SafeRelative(item.SourcePath);
        var first = rel.IndexOf('\\');
        if (first > 0 && rel[..first].Equals(item.Category, StringComparison.OrdinalIgnoreCase)) rel = rel[(first + 1)..];
        return Path.Combine(item.Category, rel);
    }

    static string? RelativeTo(string path, string prefix)
    {
        if (prefix.Length == 0) return path;
        return path.Length > prefix.Length + 1 && path.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase) ? path[(prefix.Length + 1)..] : null;
    }

    static bool IsUnder(string rel, string folder) => rel.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase);

    static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };
    static readonly char[] Invalid = ['<', '>', ':', '"', '|', '?', '*', '/'];

    /// <summary>A path that is valid on NTFS/exFAT (Linux names may contain ':' '?' or end in a dot).</summary>
    internal static string SafeRelative(string path) =>
        string.Join('\\', path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Select(SafeName));

    internal static string SafeName(string name)
    {
        var chars = name.Select(c => c < 32 || Invalid.Contains(c) ? '_' : c).ToArray();
        var s = new string(chars).TrimEnd('.', ' ');
        if (s.Length == 0) return "_";
        var stem = s.Split('.')[0];
        return ReservedNames.Contains(stem) ? "_" + s : s;
    }
}
