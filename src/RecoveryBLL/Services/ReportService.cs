using RecoveryBLL.Dtos;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;

namespace RecoveryBLL.Services;

/// <summary>
/// Classifies the catalog: per file "unique" (content exists once), "duplicate" (content exists 2+ times)
/// or "pending" (shares a size/quick hash with another file but is not fingerprinted far enough yet).
/// </summary>
public sealed class ReportService(IReportRepository reports, DiskService disks)
{
    public CatalogReport GetReport()
    {
        var mounted = disks.GetMountedRoots();
        var byId = disks.GetCatalogued().ToDictionary(d => d.Id);
        var summaries = RepositoryGuard.Run(reports.GetDiskSummaries, "Cannot build the report");
        var types = RepositoryGuard.Run(reports.GetTypeSummaries, "Cannot build the report");
        var pendingIds = RepositoryGuard.Run(reports.GetDiskIdsWithPendingFiles, "Cannot build the report");

        return new CatalogReport
        {
            Disks = summaries.Where(s => byId.ContainsKey(s.DiskId))
                             .Select(s => new DiskSummary(byId[s.DiskId], s, mounted.TryGetValue(s.DiskId, out var r) ? r : null))
                             .ToList(),
            Types = types,
            PendingUnpluggedDisks = pendingIds.Where(id => !mounted.ContainsKey(id) && byId.ContainsKey(id)).Select(id => byId[id]).ToList(),
            HasPendingOnPluggedInDisks = pendingIds.Any(mounted.ContainsKey),
        };
    }

    public IReadOnlyList<(DuplicateGroupDto Group, IReadOnlyList<(Disk? Disk, string FullPath, DateTime Modified)> Copies)> GetLargestDuplicateGroups(int top)
    {
        if (top <= 0) return [];
        var byId = disks.GetCatalogued().ToDictionary(d => d.Id);
        var mounted = disks.GetMountedRoots();
        var groups = RepositoryGuard.Run(() => reports.GetLargestDuplicateGroups(top), "Cannot list duplicates");
        return groups.Select(g => (g, (IReadOnlyList<(Disk?, string, DateTime)>)g.Locations.Select(l =>
        {
            byId.TryGetValue(l.DiskId, out var disk);
            var root = mounted.TryGetValue(l.DiskId, out var r) ? r : disk?.LastRoot ?? "?:\\";
            return (disk, Path.Combine(root, l.Path), new DateTime(l.MtimeTicks, DateTimeKind.Utc).ToLocalTime());
        }).ToList())).ToList();
    }
}
