namespace RecoveryBLL.Models;

/// <summary>Phase 3: what the junk and keep-together rules decided about a file.</summary>
public enum TagKind
{
    /// <summary>Small images, scraps, system and cache files: put aside in _Review\&lt;reason&gt;, after your own files.</summary>
    Junk,
    /// <summary>Part of a keep-together folder worth keeping (a world, a database, code): copied whole to "Apps and data".</summary>
    KeepTogether,
    /// <summary>Part of a reinstallable keep-together folder (a program, Steam, AppData): copied whole to _Review, after your own files.</summary>
    ReviewTogether,
}

/// <param name="Reason">Shown to the person and used as the folder name, e.g. "Thumbnails" or "Minecraft".</param>
/// <param name="BundleRoot">For keep-together files: the folder that is kept whole (relative to the disk root).</param>
public sealed record FileTag(TagKind Kind, string Reason, string? BundleRoot = null)
{
    /// <summary>Copied after unique files, and only into space that is left.</summary>
    public bool LowPriority => Kind != TagKind.KeepTogether;
}

/// <summary>How the copy treats junk and reinstallable folders.</summary>
public enum JunkHandling
{
    /// <summary>Copied to _Review (after your own files, only into space that is left). The default.</summary>
    SetAside,
    /// <summary>Not copied at all (still on the source disks; nothing is deleted).</summary>
    Skip,
    /// <summary>No junk detection: everything is sorted like your own files. Keep-together folders still stay whole.</summary>
    Off,
}

public sealed record TagGroup(TagKind Kind, string Reason, long Files, long Bytes);

public sealed record BundleSummary(long DiskId, string Root, string Name, TagKind Kind, long Files, long Bytes);

public sealed record TaggedFile(long DiskId, string Path, long Size);

public sealed class FileTagResult
{
    public required IReadOnlyDictionary<long, FileTag> Tags { get; init; }
    /// <summary>Per kind and reason, largest first.</summary>
    public required IReadOnlyList<TagGroup> Groups { get; init; }
    /// <summary>Every keep-together folder found, largest first.</summary>
    public required IReadOnlyList<BundleSummary> Bundles { get; init; }
    /// <summary>The largest junk files per reason (at most <see cref="FileTagger.ExamplesPerReason"/>).</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<TaggedFile>> Examples { get; init; }
    /// <summary>Pictures whose size in pixels is not known yet (image info not read): the size rules could not check them.</summary>
    public long UnmeasuredImages { get; init; }

    public static FileTagResult Empty { get; } = new()
    {
        Tags = new Dictionary<long, FileTag>(), Groups = [], Bundles = [], Examples = new Dictionary<string, IReadOnlyList<TaggedFile>>(),
    };
}
