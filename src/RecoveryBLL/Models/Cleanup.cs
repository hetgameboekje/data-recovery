namespace RecoveryBLL.Models;

// ---- Clean up: one page for duplicates and junk (agreed with the user on 2026-11-12, docs/cleanup-session-2026-11-12.md).
// The target folder only ever receives the files that are NOT wanted. The wanted version never moves out of a master.

/// <summary>Why a file leaves the folder being cleaned. Also the name of its subfolder in the target.</summary>
public enum CleanupCategory
{
    ExactCopy,
    SameVideo,
    Broken,
    SmallFile,
    ThumbnailsAndIcons,
    CachesAndSystem,
    ImageSets,
    AppFolders,
}

public static class CleanupCategories
{
    /// <summary>The order files are judged in: the first enabled category that applies wins.</summary>
    public static readonly CleanupCategory[] All = Enum.GetValues<CleanupCategory>();

    public static string FolderName(this CleanupCategory c) => c switch
    {
        CleanupCategory.ExactCopy => "Exact copies",
        CleanupCategory.SameVideo => "Same video",
        CleanupCategory.Broken => "Broken",
        CleanupCategory.SmallFile => "Small files",
        CleanupCategory.ThumbnailsAndIcons => "Thumbnails and icons",
        CleanupCategory.CachesAndSystem => "Caches and system files",
        CleanupCategory.ImageSets => "Image sets",
        CleanupCategory.AppFolders => "App folders",
        _ => c.ToString(),
    };

    public static string Explanation(this CleanupCategory c) => c switch
    {
        CleanupCategory.ExactCopy => "byte-for-byte the same as a file that stays",
        CleanupCategory.SameVideo => "the same video (picture, sound and index identical) with other, fewer or damaged tags",
        CleanupCategory.Broken => "video with missing picture data or index, or a cut-off picture",
        CleanupCategory.SmallFile => "too small to be a real photo or video, and not a complete file",
        CleanupCategory.ThumbnailsAndIcons => "thumbnail sizes, icons and game skins without camera data",
        CleanupCategory.CachesAndSystem => "Thumbs.db, desktop.ini and files in cache folders",
        CleanupCategory.ImageSets => "folders with hundreds of small images of one size (game or app parts)",
        CleanupCategory.AppFolders => "reinstallable programs and app data, moved as a whole folder",
        _ => "",
    };
}

/// <param name="Source">The folder or drive to clean (e.g. G:\Videos, or G:\From_E to merge).</param>
/// <param name="Target">Where the unwanted files go (e.g. G:\duplicates): &lt;Target&gt;\&lt;category&gt;\&lt;type&gt;\&lt;file&gt;.</param>
/// <param name="MasterRoot">Merge mode: the wanted files of <paramref name="Source"/> go to &lt;MasterRoot&gt;\&lt;type&gt; (e.g. G:\ -> G:\Videos,
/// G:\Photos). What is already in those master folders always stays. Null: clean <paramref name="Source"/> in place.</param>
public sealed record CleanupOptions(string Source, string Target, string? MasterRoot, IReadOnlySet<CleanupCategory> Categories)
{
    public const long SmallVideo = 100 * 1024;
    public const long SmallPhoto = 10 * 1024;
}

public enum CleanupAction { ToTarget, ToMaster }

/// <summary>One planned move. <see cref="Keep"/>: for a copy, the file that stays (it must still be there when moving).</summary>
public sealed record CleanupItem(
    CleanupAction Action, CleanupCategory? Category, string Type, string SourcePath, string DestPath,
    long Size, long MtimeTicks, string Reason, string? Keep);

/// <summary>A case the person decides: e.g. the master copy is broken and the copy in the source is complete.</summary>
public sealed record CleanupDecision(string SourcePath, string MasterPath, string Question);

public sealed class CleanupPlan
{
    public required CleanupOptions Options { get; init; }
    /// <summary>Normalised paths that were checked.</summary>
    public required string Source { get; init; }
    public required string Target { get; init; }
    public string? MasterRoot { get; init; }
    public required IReadOnlyList<CleanupItem> Items { get; init; }
    public required IReadOnlyList<CleanupDecision> Decisions { get; init; }
    public long FilesChecked { get; init; }
    public long Unreadable { get; init; }
    public IEnumerable<CleanupItem> ToTarget => Items.Where(i => i.Action == CleanupAction.ToTarget);
    public IEnumerable<CleanupItem> ToMaster => Items.Where(i => i.Action == CleanupAction.ToMaster);
}

public sealed record CleanupRunResult(long Moved, long MovedBytes, long Skipped, long Errors, bool Completed, string? LogFile);

/// <summary>One line of the undo record: where a file was, where it went.</summary>
public sealed record CleanupLogEntry(string From, string To, long Size);
