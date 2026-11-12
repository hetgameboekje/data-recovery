using System.Diagnostics;
using System.Security.Cryptography;
using RecoveryBLL.Dtos;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;

namespace RecoveryBLL.Services;

/// <summary>One copy as it was re-read: its SHA-256, or why it could not be read.</summary>
public sealed record CopyCheck(Disk? Disk, string FullPath, long Size, string? Sha256, string? Problem);

public sealed record GroupCheck(byte[] FullHash, long Size, IReadOnlyList<CopyCheck> Copies)
{
    IEnumerable<string> Hashes => Copies.Where(c => c.Sha256 != null).Select(c => c.Sha256!);
    /// <summary>At least two copies were read and all are byte-for-byte the same.</summary>
    public bool Identical => Hashes.Count() >= 2 && Hashes.Distinct().Count() == 1;
    /// <summary>The catalog said "same content" but the files differ now (changed since the scan, or a read error).</summary>
    public bool Different => Hashes.Distinct().Count() > 1;
}

public sealed record DuplicateCheckResult(IReadOnlyList<GroupCheck> Groups, long BytesRead, bool Completed)
{
    public int Identical => Groups.Count(g => g.Identical);
    public int Different => Groups.Count(g => g.Different);
    public int NotChecked => Groups.Count(g => !g.Identical && !g.Different);
}

/// <summary>
/// "Are the duplicates really duplicates?" Re-reads every copy of a sample of duplicate groups completely and compares
/// them with SHA-256: a different algorithm from the catalog's xxHash128, so it is an independent check.
/// Read-only. Only plugged-in disks can be checked; copies on unplugged disks are reported as not read.
/// </summary>
public sealed class DuplicateCheckService(IReportRepository reports, DiskService disks, IFileContentReader reader, IOperationLog log)
{
    public const long DefaultMaxFileSize = 500L << 20;      // skip huge files in a sample: they cost minutes each
    public const long DefaultReadBudget = 2L << 30;         // stop after reading about 2 GB

    public DuplicateCheckResult CheckSample(int groups, IProgress<ProgressInfo>? progress, CancellationToken ct,
                                            long maxFileSize = DefaultMaxFileSize, long readBudget = DefaultReadBudget)
    {
        var mounted = disks.GetMountedRoots();
        var byId = disks.GetCatalogued().ToDictionary(d => d.Id);
        // Ask for more than needed and prefer groups that are fully readable now.
        var candidates = RepositoryGuard.Run(() => reports.GetDuplicateGroupSample(Math.Max(groups * 4, groups), maxFileSize), "Cannot read the catalog");
        var chosen = candidates.OrderByDescending(g => g.Locations.All(l => mounted.ContainsKey(l.DiskId))).Take(groups).ToList();
        return Check(chosen, mounted, byId, progress, ct, readBudget);
    }

    DuplicateCheckResult Check(IReadOnlyList<DuplicateGroupDto> groups, IReadOnlyDictionary<long, string> mounted,
                               IReadOnlyDictionary<long, Disk> byId, IProgress<ProgressInfo>? progress, CancellationToken ct, long readBudget)
    {
        var results = new List<GroupCheck>();
        long read = 0, done = 0;
        long totalBytes = groups.Sum(g => g.Size * g.Locations.Count(l => mounted.ContainsKey(l.DiskId)));
        var sw = Stopwatch.StartNew();
        log.Info($"Checking {groups.Count} duplicate groups: every copy is re-read and compared with SHA-256.");
        foreach (var g in groups)
        {
            if (ct.IsCancellationRequested || read >= readBudget) break;
            var copies = new List<CopyCheck>();
            foreach (var l in g.Locations)
            {
                byId.TryGetValue(l.DiskId, out var disk);
                if (!mounted.TryGetValue(l.DiskId, out var root))
                {
                    copies.Add(new CopyCheck(disk, Path.Combine(disk?.LastRoot ?? "?:\\", l.Path), g.Size, null, "disk not plugged in"));
                    continue;
                }
                var full = Path.Combine(root, l.Path);
                try
                {
                    using var s = reader.OpenRead(full, sequential: true);
                    var sha = SHA256.HashData(s);
                    read += s.Length;
                    copies.Add(new CopyCheck(disk, full, s.Length, Convert.ToHexString(sha).ToLowerInvariant(),
                        s.Length != g.Size ? $"size is {s.Length:N0}, the catalog says {g.Size:N0}" : null));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    copies.Add(new CopyCheck(disk, full, g.Size, null, ex.Message));
                }
                progress?.Report(new ProgressInfo("Checking duplicates", done, groups.Count, read, totalBytes, sw.Elapsed));
            }
            var result = new GroupCheck(g.FullHash, g.Size, copies);
            results.Add(result);
            done++;
            if (result.Different)
                log.Warn($"NOT identical although the catalog says so ({Format.Bytes(g.Size)}): " +
                         string.Join(" | ", copies.Select(c => $"{c.FullPath} {c.Sha256?[..12]}")) + ". Scan those disks again.");
        }
        var summary = new DuplicateCheckResult(results, read, !ct.IsCancellationRequested);
        progress?.Report(new ProgressInfo("Checking duplicates", done, groups.Count, read, totalBytes, sw.Elapsed));
        log.Info($"Duplicate check: {summary.Identical} identical, {summary.Different} different, {summary.NotChecked} not checked; read {Format.Bytes(read)}.");
        return summary;
    }
}
