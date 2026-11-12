using RecoveryBLL.Dtos;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;

namespace RecoveryBLL.Services;

/// <summary>
/// Phase 2b: builds the WinDirStat-style map from the catalog only. No disk is read, so it works for unplugged disks
/// and never touches a file. Folders, sizes, file types, extra copies and not-yet-fingerprinted files.
/// </summary>
public sealed class DiskMapService(IReportRepository reports, DiskService disks, DictionaryService dictionaries)
{
    /// <summary>Files smaller than 1/<c>MaxBlocks</c> of the map are merged into one block per folder.</summary>
    public const int MaxBlocks = 250_000;

    /// <param name="diskId">One disk, or null for every catalogued disk (one top-level block per disk).</param>
    public DiskMap Build(long? diskId, CancellationToken ct)
    {
        var dict = dictionaries.Current;
        var names = dict.CategoryOrder.Count > 0 ? dict.CategoryOrder.ToList() : [dict.DefaultCategory];
        var categoryIndex = names.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i, StringComparer.OrdinalIgnoreCase);
        int defaultCategory = categoryIndex.TryGetValue(dict.DefaultCategory, out var dc) ? dc : names.Count - 1;

        var catalogued = disks.GetCatalogued().ToDictionary(d => d.Id);
        var mounted = disks.GetMountedRoots();
        string DiskName(long id)
        {
            if (!catalogued.TryGetValue(id, out var d)) return $"Disk {id}";
            var letter = mounted.TryGetValue(id, out var r) ? r.TrimEnd('\\') + "  " : "";
            return letter + d.DisplayName + (mounted.ContainsKey(id) ? "" : "  (not plugged in)");
        }

        MapNode root = diskId is { } one
            ? new MapNode { Name = DiskName(one), Kind = MapNodeKind.Disk, DiskId = one }
            : new MapNode { Name = "All catalogued disks", Kind = MapNodeKind.Root };
        var diskNodes = new Dictionary<long, MapNode>();
        if (diskId is { } only) diskNodes[only] = root;

        var extensions = new Dictionary<string, (int Cat, long Bytes, long Files, long Extra)>(StringComparer.OrdinalIgnoreCase);
        var categoryTotals = new long[names.Count];
        var categoryExtra = new long[names.Count];

        RepositoryGuard.Run(() => reports.ReadMapFiles(diskId, f =>
        {
            int cat = f.MediaType != null && categoryIndex.TryGetValue(f.MediaType, out var c) ? c : defaultCategory;
            if (!diskNodes.TryGetValue(f.DiskId, out var disk))
            {
                disk = new MapNode { Name = DiskName(f.DiskId), Kind = MapNodeKind.Disk, DiskId = f.DiskId, Parent = root };
                Child(root, disk.Name + "\0" + f.DiskId, disk);
                diskNodes[f.DiskId] = disk;
            }

            var parts = f.Path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;
            var folder = disk;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                folder.Index ??= new(StringComparer.OrdinalIgnoreCase);
                if (!folder.Index.TryGetValue(parts[i], out var next) || !next.IsContainer)
                {
                    next = new MapNode { Name = parts[i], Kind = MapNodeKind.Folder, DiskId = f.DiskId, Parent = folder };
                    Child(folder, parts[i], next);
                }
                folder = next;
            }

            var ext = (f.Ext ?? "").TrimStart('.').ToLowerInvariant();
            var file = new MapNode
            {
                Name = parts[^1], Kind = MapNodeKind.File, DiskId = f.DiskId, Parent = folder, Ext = ext, State = f.State,
                Bytes = f.Size, Files = 1, ModifiedTicks = f.MtimeTicks, Category = cat,
                ExtraCopyBytes = f.State == MapFileState.ExtraCopy ? f.Size : 0,
                PendingBytes = f.State == MapFileState.Pending ? f.Size : 0,
            };
            folder.Children.Add(file);   // files are not looked up by name, so they skip the index

            for (var n = folder; n != null; n = n.Parent)
            {
                n.Bytes += f.Size;
                n.Files++;
                n.ExtraCopyBytes += file.ExtraCopyBytes;
                n.PendingBytes += file.PendingBytes;
                if (f.MtimeTicks > n.ModifiedTicks) n.ModifiedTicks = f.MtimeTicks;
                (n.CategoryBytes ??= new long[names.Count])[cat] += f.Size;
            }

            var e = extensions.TryGetValue(ext, out var x) ? x : (cat, 0L, 0L, 0L);
            extensions[ext] = (e.Item1, e.Item2 + f.Size, e.Item3 + 1, e.Item4 + file.ExtraCopyBytes);
            categoryTotals[cat] += f.Size;
            categoryExtra[cat] += file.ExtraCopyBytes;
        }, ct), "Cannot read the catalog for the disk map");

        ct.ThrowIfCancellationRequested();
        long mergeBelow = Math.Max(1, root.Bytes / MaxBlocks);
        Finish(root, mergeBelow, ct);

        return new DiskMap
        {
            Root = root,
            Categories = names.Select((n, i) => new MapCategory(n, dict.CategoryColors.TryGetValue(n, out var col) ? col : null,
                                                                categoryTotals[i], categoryExtra[i])).ToList(),
            Extensions = extensions.Select(kv => new MapExtension(kv.Key, kv.Value.Cat, kv.Value.Bytes, kv.Value.Files, kv.Value.Extra))
                                   .OrderByDescending(e => e.Bytes).ToList(),
            MergedBelowBytes = mergeBelow,
        };
    }

    static void Child(MapNode parent, string key, MapNode child)
    {
        (parent.Index ??= new(StringComparer.OrdinalIgnoreCase))[key] = child;
        parent.Children.Add(child);
    }

    /// <summary>Merges tiny files per folder, sorts largest first, picks each folder's colour and drops the build-only data.</summary>
    static void Finish(MapNode root, long mergeBelow, CancellationToken ct)
    {
        var stack = new Stack<MapNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var n = stack.Pop();
            var small = n.Children.Where(c => c.Kind == MapNodeKind.File && c.Bytes < mergeBelow).ToList();
            if (small.Count > 1)
            {
                n.Children.RemoveAll(c => c.Kind == MapNodeKind.File && c.Bytes < mergeBelow);
                var bySize = new long[n.CategoryBytes!.Length];
                foreach (var s in small) bySize[s.Category] += s.Bytes;
                n.Children.Add(new MapNode
                {
                    Name = $"{small.Count:N0} small files", Kind = MapNodeKind.SmallFiles, DiskId = n.DiskId, Parent = n,
                    Bytes = small.Sum(s => s.Bytes), Files = small.Count, ModifiedTicks = small.Max(s => s.ModifiedTicks),
                    ExtraCopyBytes = small.Sum(s => s.ExtraCopyBytes), PendingBytes = small.Sum(s => s.PendingBytes),
                    Category = Array.IndexOf(bySize, bySize.Max()),
                });
            }
            n.Children.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
            if (n.CategoryBytes is { } cb) n.Category = Array.IndexOf(cb, cb.Max());
            n.CategoryBytes = null;
            n.Index = null;
            foreach (var c in n.Children)
                if (c.IsContainer) stack.Push(c);
        }
    }
}
