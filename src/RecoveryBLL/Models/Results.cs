using RecoveryBLL.Dtos;

namespace RecoveryBLL.Models;

/// <summary>Progress of a long operation, reported through IProgress&lt;ProgressInfo&gt;.</summary>
public sealed record ProgressInfo(string Stage, long Done, long Total, long Bytes, long TotalBytes, TimeSpan Elapsed)
{
    public double Fraction => Total > 0 ? Math.Clamp((double)Done / Total, 0, 1) : 0;
    public double BytesPerSecond => Elapsed.TotalSeconds > 0 ? Bytes / Elapsed.TotalSeconds : 0;
}

public sealed record ScanRequest(string Path, IReadOnlyCollection<string> Excludes, VolumeIdentity? IdentityOverride = null);

public sealed record ScanResult(string Path, long DiskId, long Files, long Bytes, long FolderErrors, int MarkedMissing, bool Completed);

public sealed record FingerprintOptions(int ThreadsPerDisk = 1, bool Sha256 = false, bool ReadImageInfo = true, bool AutoThreads = false);

public sealed record StageResult(string Stage, long Files, long Bytes, long Errors, bool Completed);

public sealed class DiskSummary(Disk disk, DiskSummaryDto dto, string? mountedRoot)
{
    public Disk Disk { get; } = disk;
    public string? MountedRoot { get; } = mountedRoot;
    public long Files { get; } = dto.Files;
    public long TotalBytes { get; } = dto.TotalBytes;
    public long OnlyCopyBytes { get; } = dto.OnlyCopyBytes;
    public long DuplicatedBytes { get; } = dto.DuplicatedBytes;
    public long DuplicatedOnOtherDiskBytes { get; } = dto.DuplicatedOnOtherDiskBytes;
    public long PendingBytes { get; } = dto.PendingBytes;
    public long EmptyFiles { get; } = dto.EmptyFiles;
    public long ErrorFiles { get; } = dto.ErrorFiles;
}

/// <summary>The answer to "how much unique data is there, and does it fit?".</summary>
public sealed class CatalogReport
{
    public required IReadOnlyList<DiskSummary> Disks { get; init; }
    public required IReadOnlyList<TypeSummaryDto> Types { get; init; }
    public long TotalFiles => Types.Sum(t => t.Files);
    public long TotalBytes => Types.Sum(t => t.TotalBytes);
    public long UniqueBytes => Types.Sum(t => t.UniqueBytes);
    public long RedundantBytes => Types.Sum(t => t.RedundantBytes);
    public long PendingBytes => Types.Sum(t => t.PendingBytes);
    /// <summary>Space for one copy of everything: between UniqueBytes and this, until pending files are fingerprinted.</summary>
    public long NeededMaxBytes => UniqueBytes + PendingBytes;
    /// <summary>Disks that hold files still waiting for fingerprinting but are not plugged in.</summary>
    public required IReadOnlyList<Disk> PendingUnpluggedDisks { get; init; }
    public bool HasPendingOnPluggedInDisks { get; init; }
}

public sealed record MergeResult(string Source, int Disks, int Files);

public sealed record LinuxMount(int DiskNumber, int Partition, string FileSystem, string Path, VolumeIdentity Identity);
