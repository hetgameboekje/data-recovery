using RecoveryBLL.Dtos;
using RecoveryBLL.Exceptions;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;

namespace RecoveryBLL.Services;

/// <summary>
/// Merges a catalog file made on another PC into the configured catalog (any provider).
/// Same file version on both sides: fingerprints are combined. Otherwise the most recent observation wins.
/// </summary>
public sealed class MergeService(ICatalogSourceFactory sources, ICatalogExchange target, DiskService disks, IOperationLog log)
{
    const int BatchSize = 5000;

    public MergeResult Merge(string catalogFile, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        if (!File.Exists(catalogFile)) throw new RecoveryServiceException("Catalog file not found: " + catalogFile);
        var source = RepositoryGuard.Run(() => sources.OpenCatalogFile(catalogFile), "Cannot open " + catalogFile);

        var diskIds = new Dictionary<(string, long), long>();
        var batch = new List<CatalogRecordDto>(BatchSize);
        int merged = 0;
        long seen = 0;
        var started = DateTime.UtcNow;

        IEnumerator<CatalogRecordDto> records = RepositoryGuard.Run(() => source.ReadAll().GetEnumerator(), "Cannot read " + catalogFile);
        using var _ = records;
        while (RepositoryGuard.Run(records.MoveNext, "Cannot read " + catalogFile))   // streamed: never the whole catalog in memory
        {
            var r = records.Current;
            if (ct.IsCancellationRequested) break;
            var key = (r.DiskSerial, r.DiskTotalBytes);
            if (!diskIds.TryGetValue(key, out var id))
            {
                id = disks.EnsureDisk(new VolumeIdentity(r.DiskSerial, r.DiskTotalBytes, r.DiskLabel ?? "", r.DiskFileSystem ?? "", ""));
                diskIds[key] = id;
            }
            r.DiskId = id;
            batch.Add(r);
            seen++;
            if (batch.Count >= BatchSize) Flush();
        }
        Flush();
        progress?.Report(new ProgressInfo("Merge", seen, seen, 0, 0, DateTime.UtcNow - started));

        var result = new MergeResult(catalogFile, diskIds.Count, merged);
        log.Info($"Merged {catalogFile}: {result.Disks:N0} disk(s), {result.Files:N0} file record(s) added or updated.");
        return result;

        void Flush()
        {
            if (batch.Count == 0) return;
            merged += RepositoryGuard.Run(() => target.MergeRecords(batch), "Cannot merge into the catalog");
            batch.Clear();
            progress?.Report(new ProgressInfo("Merge", seen, 0, 0, 0, DateTime.UtcNow - started));
        }
    }
}
