using RecoveryBLL.Dtos;

namespace RecoveryBLL.Interfaces;

// Repository interfaces: the BLL says WHAT it needs, the DAL decides HOW (SQLite, SQL Server, ...).
// Split by responsibility (interface segregation) so a service only depends on what it uses.

/// <summary>Creates/upgrades the schema of the configured catalog database.</summary>
public interface ICatalogStore
{
    string Description { get; }
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
