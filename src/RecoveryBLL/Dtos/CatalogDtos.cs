namespace RecoveryBLL.Dtos;

// DTOs cross the BLL <-> DAL boundary. They are plain data; the repositories fill them, the services read them.

public sealed class DiskDto
{
    public long Id { get; set; }
    public required string Serial { get; set; }
    public long TotalBytes { get; set; }
    public string? Label { get; set; }
    public string? FileSystem { get; set; }
    public string? LastRoot { get; set; }
    public required string FirstSeen { get; set; }
    public string? LastScan { get; set; }
    public long FileCount { get; set; }
}

/// <summary>One file found while walking a folder. Path is relative to the volume root.</summary>
public sealed record ScannedFileDto(string Path, long Size, long MtimeTicks, string Ext, string MediaType);

/// <summary>A file that needs work in a fingerprint stage.</summary>
public sealed record FileJobDto(long Id, string Path, long Size, string Ext);

public enum FingerprintResultKind { QuickHash, FullHash, Media, Error, Missing }

/// <summary>The outcome of one fingerprint step for one file.</summary>
public sealed class FingerprintResultDto
{
    public long Id { get; init; }
    public FingerprintResultKind Kind { get; init; }
    public byte[]? QuickHash { get; init; }
    public byte[]? FullHash { get; init; }
    public byte[]? Sha256 { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? Camera { get; init; }
    public string? Taken { get; init; }
    public string? Error { get; init; }
}

/// <summary>A complete catalog row including its disk identity; used to merge catalogs (also across providers).</summary>
public sealed class CatalogRecordDto
{
    public required string DiskSerial { get; init; }
    public long DiskTotalBytes { get; init; }
    public string? DiskLabel { get; init; }
    public string? DiskFileSystem { get; init; }
    public long DiskId { get; set; }                 // target disk id, filled in by the merge service
    public required string Path { get; init; }
    public long Size { get; init; }
    public long Mtime { get; init; }
    public string? Ext { get; init; }
    public string? MediaType { get; init; }
    public byte[]? QuickHash { get; init; }
    public byte[]? FullHash { get; init; }
    public byte[]? Sha256 { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public long? DurationMs { get; init; }
    public string? Camera { get; init; }
    public string? Taken { get; init; }
    public bool MediaDone { get; init; }
    public bool? OpensOk { get; init; }
    public string? Error { get; init; }
    public required string FirstSeen { get; init; }
    public required string LastSeen { get; init; }
    public bool Missing { get; init; }
}

public sealed class DiskSummaryDto
{
    public long DiskId { get; init; }
    public long Files { get; init; }
    public long TotalBytes { get; init; }
    public long OnlyCopyBytes { get; init; }
    public long DuplicatedBytes { get; init; }
    public long DuplicatedOnOtherDiskBytes { get; init; }
    public long PendingBytes { get; init; }
    public long EmptyFiles { get; init; }
    public long ErrorFiles { get; init; }
}

public sealed class TypeSummaryDto
{
    public required string MediaType { get; init; }
    public long Files { get; init; }
    public long TotalBytes { get; init; }
    public long UniqueBytes { get; init; }
    public long RedundantBytes { get; init; }
    public long PendingBytes { get; init; }
}

public sealed class DuplicateCopyDto
{
    public long DiskId { get; init; }
    public required string Path { get; init; }
    public long MtimeTicks { get; init; }
}

public sealed class DuplicateGroupDto
{
    public required byte[] FullHash { get; init; }
    public long Size { get; init; }
    public long Copies { get; init; }
    public List<DuplicateCopyDto> Locations { get; init; } = [];
}

// ---- Phase 2: consolidation

public enum PlanFileKind { Unique, Duplicate, Pending }

/// <summary>A present, non-empty catalogued file with its classification (same rules as the report).</summary>
public sealed record PlanFileDto(long Id, long DiskId, string Path, long Size, long MtimeTicks, string? MediaType, byte[]? FullHash, PlanFileKind Kind);

/// <summary>A file that was copied: the destination row is created from the source row plus these hashes.</summary>
/// <remarks><see cref="DestMtimeTicks"/> is the time as the destination stores it (FAT/exFAT round it), so a later scan sees no change.</remarks>
public sealed record CopyRecordDto(long SourceFileId, long DestDiskId, string DestPath, long DestMtimeTicks, byte[] QuickHash, byte[] FullHash, string Stamp);

// ---- Phase 2b: disk map

/// <summary>How a file shows on the disk map. Kept = the copy counted as "the" content of a duplicate group.</summary>
public enum MapFileState : byte { Unique, Kept, ExtraCopy, Pending }

/// <summary>Phase 3: what the junk and keep-together rules look at (no disk access needed).</summary>
public readonly record struct FileFactsDto(long Id, long DiskId, string Path, string? Ext, long Size, string? MediaType,
                                           int? Width, int? Height, bool HasCamera);

public readonly record struct MapFileDto(long DiskId, string Path, long Size, long MtimeTicks, string? MediaType, string? Ext, MapFileState State);
