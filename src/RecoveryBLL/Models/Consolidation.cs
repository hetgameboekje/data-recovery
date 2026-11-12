namespace RecoveryBLL.Models;

// Phase 2: consolidate every catalogued disk onto the destination disks (main first, then overflow).

/// <summary>What happens to one catalogued file.</summary>
public enum CopyAction
{
    /// <summary>Only/best copy of its content: copied to the main folders (Photos, Videos, ...).</summary>
    Keep,
    /// <summary>Same folder and name as a newer file with different content: copied to _OlderVersions.</summary>
    OlderVersion,
    /// <summary>Identical to a file that is kept: copied to _Duplicates (lowest priority).</summary>
    Duplicate,
    /// <summary>The same content is already on a destination disk: nothing to do.</summary>
    AlreadyThere,
    /// <summary>Identical to a file that is kept, and duplicates are switched off: not copied.</summary>
    SkippedDuplicate,
    /// <summary>Does not fit on any destination.</summary>
    NoRoom,
    /// <summary>Part of a keep-together folder (a server, database, code...): copied whole to "Apps and data", never deduplicated.</summary>
    KeepTogether,
    /// <summary>Junk or a reinstallable folder: copied to _Review, after your own files and only into space that is left.</summary>
    Review,
    /// <summary>Junk or a reinstallable folder, and setting it aside is switched off: not copied (still on the source disk).</summary>
    SkippedReview,
}

/// <summary>A destination drive or folder. <see cref="MaxBytes"/> caps what this run may write there.</summary>
public sealed record DestinationRequest(string Path, long? MaxBytes = null, VolumeIdentity? IdentityOverride = null);

public sealed record ConsolidateOptions(
    IReadOnlyList<DestinationRequest> Destinations,
    bool CopyDuplicates = true,
    long ReserveBytes = ConsolidateOptions.DefaultReserve,
    bool AllowPending = false,
    bool Verify = false,
    string? ApproveDeleteCode = null,
    bool WaitForUnplugged = true,
    JunkHandling Junk = JunkHandling.SetAside)
{
    /// <summary>How long a copy waits for an unplugged disk to come back before it skips that disk.</summary>
    public TimeSpan UnplugWait { get; init; } = TimeSpan.FromMinutes(30);

    public const long DefaultReserve = 1L << 30;   // 1 GB kept free on every destination
    /// <summary>On the Windows drive at least this stays free, so Windows (updates, page file, temp) keeps working.</summary>
    public const long SystemDriveMinFree = 20L << 30;
}

/// <summary>One destination as the planner sees it.</summary>
public sealed class DestinationPlan
{
    public required long DiskId { get; init; }
    public required string Name { get; init; }
    /// <summary>Volume root, e.g. "H:\". Updated when an unplugged destination comes back under another letter.</summary>
    public required string Root { get; set; }
    /// <summary>Folder inside the volume where the copy goes ("" = the root itself).</summary>
    public required string Prefix { get; init; }
    public required string FileSystem { get; init; }
    public bool IsSystemDrive { get; init; }
    public long ReserveBytes { get; init; }
    public long FreeBytes { get; init; }
    /// <summary>What this run may use: free space minus the reserve, capped by MaxBytes.</summary>
    public long UsableBytes { get; init; }
    public long PlannedBytes { get; set; }
    public long PlannedFiles { get; set; }
    /// <summary>Old _Duplicates files removed to make room for unique files.</summary>
    public long EvictedBytes { get; set; }
    public bool IsFat => FileSystem.StartsWith("FAT", StringComparison.OrdinalIgnoreCase) && !FileSystem.Equals("exFAT", StringComparison.OrdinalIgnoreCase);
    public string FullPath => Prefix.Length == 0 ? Root : Path.Combine(Root, Prefix);
}

public sealed class PlanItem
{
    public required long FileId { get; init; }
    public required long SourceDiskId { get; init; }
    public required string SourcePath { get; init; }
    public required long Size { get; init; }
    public required long MtimeTicks { get; init; }
    public required string Category { get; init; }
    public byte[]? FullHash { get; init; }
    public CopyAction Action { get; set; }
    public long? DestDiskId { get; set; }
    /// <summary>Relative to the destination volume root (prefix included).</summary>
    public string? DestPath { get; set; }
    public string? Note { get; set; }
    public bool NeedsCopy => (Action is CopyAction.Keep or CopyAction.OlderVersion or CopyAction.Duplicate or CopyAction.KeepTogether or CopyAction.Review)
                             && DestDiskId != null;
}

/// <summary>A file in a destination's _Duplicates folder that will be deleted to make room for a unique file.</summary>
/// <summary><see cref="KeptDiskId"/>/<see cref="KeptPath"/>: the copy outside _Duplicates that is checked again right before deleting.</summary>
public sealed record PlanEviction(long FileId, long DiskId, string Path, long Size, long KeptDiskId, string KeptPath);

public sealed class CopyPlan
{
    public required IReadOnlyList<PlanItem> Items { get; init; }
    public required IReadOnlyList<PlanEviction> Evictions { get; init; }
    public required IReadOnlyList<DestinationPlan> Destinations { get; init; }
    /// <summary>Files that could not be classified because they are not fingerprinted far enough (treated as unique).</summary>
    public long PendingFiles { get; init; }
    public long PendingBytes { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    /// <summary>The evictions as a list to approve (step 1 of the two-step deletion check). Empty when nothing is deleted.</summary>
    public DeletionBatch Deletions { get; init; } = DeletionBatch.Empty;
    /// <summary>Planned destination paths longer than 260 characters (Explorer and older tools may not open them).</summary>
    public long LongPaths { get; init; }
    /// <summary>Catalogued files elsewhere on a destination disk (outside the destination folder): on that disk already, not copied.</summary>
    public long SameDiskFiles { get; init; }
    public long SameDiskBytes { get; init; }
    /// <summary>Source disks that hold files to copy but are not plugged in: (disk, files, bytes).</summary>
    public IReadOnlyList<(Disk Disk, long Files, long Bytes)> WaitingDisks { get; init; } = [];

    public long Files(CopyAction a) => Items.Count(i => i.Action == a);
    public long Bytes(CopyAction a) => Items.Where(i => i.Action == a).Sum(i => i.Size);
    public long BytesToCopy => Items.Where(i => i.NeedsCopy).Sum(i => i.Size);
    public long FilesToCopy => Items.Count(i => i.NeedsCopy);
    static bool IsDuplicateNoRoom(PlanItem i) => i.Action == CopyAction.NoRoom && i.Note == PlanNotes.DuplicateNoRoom;
    static bool IsReviewNoRoom(PlanItem i) => i.Action == CopyAction.NoRoom && i.Note == PlanNotes.ReviewNoRoom;
    static bool IsUniqueNoRoom(PlanItem i) => i.Action == CopyAction.NoRoom && !IsDuplicateNoRoom(i) && !IsReviewNoRoom(i);
    /// <summary>All unique content (kept, older versions, keep-together folders, and unique files that found no room).</summary>
    public long UniqueBytes => Bytes(CopyAction.Keep) + Bytes(CopyAction.OlderVersion) + Bytes(CopyAction.KeepTogether)
                               + Items.Where(IsUniqueNoRoom).Sum(i => i.Size);
    /// <summary>All identical extra copies, whether copied, switched off or out of room.</summary>
    public long DuplicateBytes => Bytes(CopyAction.Duplicate) + Bytes(CopyAction.SkippedDuplicate) + Items.Where(IsDuplicateNoRoom).Sum(i => i.Size);
    /// <summary>Junk and reinstallable folders, whether set aside, switched off or out of room.</summary>
    public long ReviewBytes => Bytes(CopyAction.Review) + Bytes(CopyAction.SkippedReview) + Items.Where(IsReviewNoRoom).Sum(i => i.Size);
    public long ReviewFiles => Files(CopyAction.Review) + Files(CopyAction.SkippedReview) + Items.Count(IsReviewNoRoom);
    public long UniqueNoRoomBytes => Items.Where(IsUniqueNoRoom).Sum(i => i.Size);
    public long DuplicatesNoRoom => Items.Count(IsDuplicateNoRoom);
    public long ReviewNoRoom => Items.Count(IsReviewNoRoom);
    public long UsableBytes => Destinations.Sum(d => d.UsableBytes);

    /// <summary>Everything except duplicates and set-aside files fits.</summary>
    public bool UniqueFits => !Items.Any(IsUniqueNoRoom);
}

public static class PlanNotes
{
    public const string DuplicateNoRoom = "duplicate: no room left";
    public const string ReviewNoRoom = "set aside: no room left";
}

public sealed record CopyResult(
    long Copied, long CopiedBytes, long AlreadyThere, long WaitingForDisk, long NoRoom, long Errors,
    long Evicted, long EvictedBytes, bool Completed)
{
    /// <summary>Old duplicates that would have made room, but deleting them was not approved/confirmed.</summary>
    public long DeletionsNotApproved { get; init; }
    /// <summary>Source disks skipped because reads kept timing out (failing disk: image it first).</summary>
    public IReadOnlyList<string> FailingDisks { get; init; } = [];
}
