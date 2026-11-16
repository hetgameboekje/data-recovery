using RecoveryBLL.Dtos;

namespace RecoveryBLL.Interfaces;

// Repository interfaces: the BLL says WHAT it needs, the DAL decides HOW (SQLite, SQL Server, ...).
// Split by responsibility (interface segregation) so a service only depends on what it uses.

/// <summary>Creates/upgrades the schema of the configured catalog database.</summary>
public interface ICatalogStore
{
    string Description { get; }
    /// <summary>The catalog file on this PC (SQLite), or null for a server database.</summary>
    string? LocalFile => null;
    void EnsureCreated();
}

public interface IDiskRepository
{
    IReadOnlyList<DiskDto> GetAll();
    DiskDto? GetById(long id);
    DiskDto? FindByIdentity(string serial, long totalBytes);
    long Add(DiskDto dto);
    void UpdateSeen(long id, string label, string fileSystem, string root);
    void MarkScanCompleted(long id, string stamp);
}

public enum FingerprintStage { QuickHash, FullHash, ImageInfo }

public interface IFileRepository
{
    /// <summary>Insert new files; refresh known ones. Keeps fingerprints of files whose size and modified time are unchanged.</summary>
    void UpsertScanned(long diskId, IReadOnlyList<ScannedFileDto> batch, string stamp);

    /// <summary>After a complete walk of <paramref name="pathPrefix"/>: flag files under it that were not seen.</summary>
    int MarkMissingNotSeen(long diskId, string pathPrefix, string stamp);

    /// <summary>Files on this disk that need the given stage (the size/quick-hash funnel lives in the query).</summary>
    IReadOnlyList<FileJobDto> GetJobs(long diskId, FingerprintStage stage, bool includeSha256, IReadOnlyCollection<string> imageExtensions);

    void SaveResults(IReadOnlyList<FingerprintResultDto> results);
}

public interface IReportRepository
{
    IReadOnlyList<DiskSummaryDto> GetDiskSummaries();
    IReadOnlyList<TypeSummaryDto> GetTypeSummaries();
    IReadOnlyList<long> GetDiskIdsWithPendingFiles();
    IReadOnlyList<DuplicateGroupDto> GetLargestDuplicateGroups(int top);
    /// <summary>A random sample of duplicate groups whose file size is at most <paramref name="maxSize"/>, with every copy.</summary>
    IReadOnlyList<DuplicateGroupDto> GetDuplicateGroupSample(int count, long maxSize);

    /// <summary>
    /// Phase 2b: every present, non-empty file of one disk (or of all disks when null), classified like the report.
    /// Streamed row by row, so a big catalog is never held twice in memory.
    /// </summary>
    void ReadMapFiles(long? diskId, Action<MapFileDto> row, CancellationToken ct);

    /// <summary>Phase 3: every present, non-empty file with what the junk/keep-together rules need. Streamed.</summary>
    void ReadFileFacts(Action<FileFactsDto> row, CancellationToken ct);
}

/// <summary>Reading a whole catalog (merge source) and merging records into one (merge target).</summary>
public interface ICatalogExchange
{
    IEnumerable<CatalogRecordDto> ReadAll();
    int MergeRecords(IReadOnlyList<CatalogRecordDto> records);
}

/// <summary>Opens another catalog file (e.g. made on the other PC) as a merge source.</summary>
public interface ICatalogSourceFactory
{
    ICatalogExchange OpenCatalogFile(string path);
}

/// <summary>Phase 2: what the copy planner reads, and what the copier records.</summary>
public interface IConsolidationRepository
{
    /// <summary>Every present, non-empty file, classified unique / duplicate / pending.</summary>
    IReadOnlyList<PlanFileDto> GetPlanFiles();

    /// <summary>Adds (or refreshes) the destination file in the catalog, copying the source's metadata, and stores the hashes on the source too.</summary>
    void RecordCopy(CopyRecordDto copy);

    /// <summary>A destination file that was deleted (eviction of an old duplicate).</summary>
    void MarkMissing(long fileId);
}

/// <summary>
/// Clean up: keeps the catalog in step when a file is moved or removed, so it never shows files that are gone.
/// The catalog does not decide what is a duplicate or broken: the Clean up page reads the folders themselves.
/// </summary>
public interface ICatalogSync
{
    /// <summary>The file at (disk, path) was moved. No-op when it is not catalogued. The row keeps its fingerprints.</summary>
    void RecordMove(long diskId, string path, long newDiskId, string newPath, long newMtimeTicks, string stamp);

    /// <summary>The file at (disk, path) is gone (deleted, or moved to a disk the catalog does not know).</summary>
    void MarkMissing(long diskId, string path);
}
