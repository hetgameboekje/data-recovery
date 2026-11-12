using RecoveryBLL.Dtos;
using RecoveryBLL.Interfaces;
using static RecoveryDAL.SqlServer.SqlServerDb;

namespace RecoveryDAL.SqlServer;

/// <summary>Same classification as the SQLite version (see SqliteReportRepository), in T-SQL.</summary>
public sealed class SqlServerReportRepository(SqlServerDb db) : IReportRepository
{
    internal const string Classified = """
        WITH f AS (
            SELECT id, disk_id, size, media_type, quick_hash, full_hash,
                   COUNT_BIG(*) OVER (PARTITION BY size) AS n_size,
                   COUNT_BIG(*) OVER (PARTITION BY size, quick_hash) AS n_quick
            FROM dbo.files WHERE missing = 0 AND size > 0),
        s AS (
            SELECT *, CASE
                WHEN n_size = 1 THEN 'unique'
                WHEN quick_hash IS NULL THEN 'pending'
                WHEN n_quick = 1 THEN 'unique'
                WHEN full_hash IS NULL THEN 'pending'
                ELSE 'hashed' END AS status
            FROM f),
        h AS (
            SELECT full_hash, COUNT_BIG(*) AS copies, COUNT_BIG(DISTINCT disk_id) AS on_disks
            FROM s WHERE status = 'hashed' GROUP BY full_hash),
        c AS (
            SELECT s.*, COALESCE(h.copies, 1) AS copies, COALESCE(h.on_disks, 1) AS on_disks,
                   CASE WHEN s.status = 'hashed' THEN ROW_NUMBER() OVER (PARTITION BY s.status, s.full_hash ORDER BY s.id) ELSE 1 END AS rn
            FROM s LEFT JOIN h ON s.status = 'hashed' AND h.full_hash = s.full_hash),
        k AS (
            SELECT id, disk_id, size, media_type, rn, on_disks,
                   CASE WHEN status = 'pending' THEN 'pending'
                        WHEN status = 'hashed' AND copies > 1 THEN 'dup'
                        ELSE 'unique' END AS kind
            FROM c)
        """;

    public IReadOnlyList<DiskSummaryDto> GetDiskSummaries() => db.Run(c =>
    {
        using var cmd = Cmd(c, Classified + """
            , agg AS (
                SELECT disk_id, COUNT_BIG(*) AS files, SUM(size) AS total,
                       SUM(CASE WHEN kind = 'unique' THEN size ELSE 0 END) AS only_copy,
                       SUM(CASE WHEN kind = 'dup' THEN size ELSE 0 END) AS dup,
                       SUM(CASE WHEN kind = 'dup' AND on_disks > 1 THEN size ELSE 0 END) AS dup_other,
                       SUM(CASE WHEN kind = 'pending' THEN size ELSE 0 END) AS pending
                FROM k GROUP BY disk_id),
            extra AS (
                SELECT disk_id,
                       SUM(CASE WHEN size = 0 THEN CAST(1 AS BIGINT) ELSE 0 END) AS empty,
                       SUM(CASE WHEN error IS NOT NULL THEN CAST(1 AS BIGINT) ELSE 0 END) AS errors
                FROM dbo.files WHERE missing = 0 GROUP BY disk_id)
            SELECT d.id, COALESCE(agg.files, 0), COALESCE(agg.total, 0), COALESCE(agg.only_copy, 0), COALESCE(agg.dup, 0),
                   COALESCE(agg.dup_other, 0), COALESCE(agg.pending, 0), COALESCE(extra.empty, 0), COALESCE(extra.errors, 0)
            FROM dbo.disks d
            LEFT JOIN agg ON agg.disk_id = d.id
            LEFT JOIN extra ON extra.disk_id = d.id
            ORDER BY d.id
            """);
        var list = new List<DiskSummaryDto>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new DiskSummaryDto
            {
                DiskId = r.GetInt64(0), Files = r.GetInt64(1), TotalBytes = r.GetInt64(2), OnlyCopyBytes = r.GetInt64(3),
                DuplicatedBytes = r.GetInt64(4), DuplicatedOnOtherDiskBytes = r.GetInt64(5), PendingBytes = r.GetInt64(6),
                EmptyFiles = r.GetInt64(7), ErrorFiles = r.GetInt64(8),
            });
        return list;
    }, "Cannot build the per-disk report");

    public IReadOnlyList<TypeSummaryDto> GetTypeSummaries() => db.Run(c =>
    {
        using var cmd = Cmd(c, Classified + """
            SELECT COALESCE(media_type, 'Other'), COUNT_BIG(*), SUM(size),
                   SUM(CASE WHEN kind = 'unique' OR (kind = 'dup' AND rn = 1) THEN size ELSE 0 END),
                   SUM(CASE WHEN kind = 'dup' AND rn > 1 THEN size ELSE 0 END),
                   SUM(CASE WHEN kind = 'pending' THEN size ELSE 0 END)
            FROM k GROUP BY COALESCE(media_type, 'Other') ORDER BY SUM(size) DESC
            """);
        var list = new List<TypeSummaryDto>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new TypeSummaryDto
            {
                MediaType = r.GetString(0), Files = r.GetInt64(1), TotalBytes = r.GetInt64(2),
                UniqueBytes = r.GetInt64(3), RedundantBytes = r.GetInt64(4), PendingBytes = r.GetInt64(5),
            });
        return list;
    }, "Cannot build the per-type report");

    public IReadOnlyList<long> GetDiskIdsWithPendingFiles() => db.Run(c =>
    {
        using var cmd = Cmd(c, Classified + "SELECT DISTINCT disk_id FROM k WHERE kind = 'pending'");
        var list = new List<long>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetInt64(0));
        return list;
    }, "Cannot list pending disks");

    public IReadOnlyList<DuplicateGroupDto> GetLargestDuplicateGroups(int top) => db.Run(c =>
    {
        var groups = new List<DuplicateGroupDto>();
        using (var cmd = Cmd(c, """
            SELECT TOP (@n) full_hash, size, COUNT_BIG(*) FROM dbo.files
            WHERE missing = 0 AND size > 0 AND full_hash IS NOT NULL
            GROUP BY full_hash, size HAVING COUNT_BIG(*) > 1
            ORDER BY size * (COUNT_BIG(*) - 1) DESC
            """, null, ("@n", top)))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) groups.Add(new DuplicateGroupDto { FullHash = (byte[])r[0], Size = r.GetInt64(1), Copies = r.GetInt64(2) });

        foreach (var g in groups)
        {
            using var cmd = Cmd(c, "SELECT disk_id, path, mtime FROM dbo.files WHERE full_hash = @h AND missing = 0 ORDER BY mtime DESC",
                null, ("@h", g.FullHash));
            using var r = cmd.ExecuteReader();
            while (r.Read()) g.Locations.Add(new DuplicateCopyDto { DiskId = r.GetInt64(0), Path = r.GetString(1), MtimeTicks = r.GetInt64(2) });
        }
        return groups;
    }, "Cannot list duplicate groups");

    public IReadOnlyList<DuplicateGroupDto> GetDuplicateGroupSample(int count, long maxSize) => db.Run(c =>
    {
        var groups = new List<DuplicateGroupDto>();
        using (var cmd = Cmd(c, """
            SELECT TOP (@n) full_hash, size, COUNT_BIG(*) FROM dbo.files
            WHERE missing = 0 AND size > 0 AND size <= @max AND full_hash IS NOT NULL
            GROUP BY full_hash, size HAVING COUNT_BIG(*) > 1
            ORDER BY NEWID()
            """, null, ("@n", count), ("@max", maxSize)))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) groups.Add(new DuplicateGroupDto { FullHash = (byte[])r[0], Size = r.GetInt64(1), Copies = r.GetInt64(2) });

        foreach (var g in groups)
        {
            using var cmd = Cmd(c, "SELECT disk_id, path, mtime FROM dbo.files WHERE full_hash = @h AND missing = 0 ORDER BY mtime DESC",
                null, ("@h", g.FullHash));
            using var r = cmd.ExecuteReader();
            while (r.Read()) g.Locations.Add(new DuplicateCopyDto { DiskId = r.GetInt64(0), Path = r.GetString(1), MtimeTicks = r.GetInt64(2) });
        }
        return groups;
    }, "Cannot sample duplicate groups");

    public void ReadMapFiles(long? diskId, Action<MapFileDto> row, CancellationToken ct) => db.Run(c =>
    {
        using var cmd = Cmd(c, Classified + """
            SELECT k.disk_id, f.path, k.size, f.mtime, k.media_type, f.ext,
                   CASE WHEN k.kind = 'pending' THEN 3 WHEN k.kind = 'dup' AND k.rn > 1 THEN 2 WHEN k.kind = 'dup' THEN 1 ELSE 0 END
            FROM k JOIN dbo.files f ON f.id = k.id
            """ + (diskId == null ? "" : " WHERE k.disk_id = @d"), null, ("@d", diskId));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            row(new MapFileDto(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetInt64(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), (MapFileState)r.GetInt32(6)));
        }
    }, "Cannot read the files for the disk map");
}
