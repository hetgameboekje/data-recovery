using RecoveryBLL.Dtos;

namespace RecoveryBLL.Models;

/// <summary>
/// Phase 2b: one block of the disk map. A disk, a folder, a file, or the merged small files of one folder
/// (so millions of files stay drawable). Built from the catalog only: unplugged disks show too.
/// </summary>
public sealed class MapNode
{
    public required string Name { get; init; }
    public MapNodeKind Kind { get; init; }
    public long DiskId { get; init; }
    public MapNode? Parent { get; internal set; }

    /// <summary>Children, largest first. Empty for files and merged blocks.</summary>
    public List<MapNode> Children { get; } = [];

    public long Bytes { get; internal set; }
    public long Files { get; internal set; }
    /// <summary>Bytes that are an extra copy of content kept elsewhere (same meaning as "extra copies" in the report).</summary>
    public long ExtraCopyBytes { get; internal set; }
    /// <summary>Bytes not fingerprinted far enough to know whether they are copies.</summary>
    public long PendingBytes { get; internal set; }
    public long ModifiedTicks { get; internal set; }

    /// <summary>Index into <see cref="DiskMap.Categories"/>; for a folder the category that takes most of its space.</summary>
    public int Category { get; internal set; }
    /// <summary>Lower-case extension of a file ("" when it has none); null for folders.</summary>
    public string? Ext { get; init; }
    public MapFileState State { get; init; }

    // Only used while building.
    internal Dictionary<string, MapNode>? Index;
    internal long[]? CategoryBytes;

    public bool IsContainer => Kind is MapNodeKind.Root or MapNodeKind.Disk or MapNodeKind.Folder;

    /// <summary>The path below the disk root (no drive letter), e.g. <c>Documenten\muziek\song.mp3</c>.</summary>
    public string RelativePath
    {
        get
        {
            var parts = new Stack<string>();
            for (var n = this; n != null && n.Kind is not (MapNodeKind.Disk or MapNodeKind.Root); n = n.Parent)
                if (n.Kind != MapNodeKind.SmallFiles) parts.Push(n.Name);
            return string.Join('\\', parts);
        }
    }

    /// <summary>From the top of the map down to this node.</summary>
    public IReadOnlyList<MapNode> Ancestry()
    {
        var list = new List<MapNode>();
        for (var n = this; n != null; n = n.Parent) list.Add(n);
        list.Reverse();
        return list;
    }

    public override string ToString() => Name;
}

public enum MapNodeKind { Root, Disk, Folder, File, SmallFiles }

public sealed record MapExtension(string Ext, int Category, long Bytes, long Files, long ExtraCopyBytes);

public sealed record MapCategory(string Name, string? Color, long Bytes, long ExtraCopyBytes);

public sealed class DiskMap
{
    public required MapNode Root { get; init; }
    /// <summary>Categories in a fixed order (index = <see cref="MapNode.Category"/>), with totals for this map.</summary>
    public required IReadOnlyList<MapCategory> Categories { get; init; }
    /// <summary>Every extension in the map, largest first.</summary>
    public required IReadOnlyList<MapExtension> Extensions { get; init; }
    /// <summary>Files smaller than this were merged into one block per folder.</summary>
    public long MergedBelowBytes { get; init; }
}
