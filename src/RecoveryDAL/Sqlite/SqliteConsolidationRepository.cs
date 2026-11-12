using RecoveryBLL.Dtos;
using RecoveryBLL.Interfaces;
using static RecoveryDAL.Sqlite.SqliteDb;

namespace RecoveryDAL.Sqlite;

public sealed class SqliteConsolidationRepository(SqliteDb db) : IConsolidationRepository
{
    public IReadOnlyList<PlanFileDto> GetPlanFiles() => db.Run(c =>
    {
        using var cmd = Cmd(c, SqliteReportRepository.Classified + """
            SELECT f.id, f.disk_id, f.path, f.size, f.mtime, f.media_type, f.full_hash, k.kind
            FROM k JOIN files f ON f.id = k.id
            """);
        var list = new List<PlanFileDto>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PlanFileDto(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt64(3), r.GetInt64(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : (byte[])r[6], Kind(r.GetString(7))));
        return list;
    }, "Cannot read the catalog for the copy plan");

    internal static PlanFileKind Kind(string k) => k switch
    {
        "dup" => PlanFileKind.Duplicate,
        "pending" => PlanFileKind.Pending,
        _ => PlanFileKind.Unique,
    };

    public void RecordCopy(CopyRecordDto copy) => db.Run(c =>
    {
        using var tx = c.BeginTransaction();
        using (var insert = Cmd(c, """
            INSERT INTO files(disk_id, path, size, mtime, ext, media_type, quick_hash, full_hash, sha256,
                              width, height, duration_ms, camera, taken, media_done, opens_ok, first_seen, last_seen)
            SELECT $dd, $dp, size, $m, ext, media_type, $q, $f, sha256,
                   width, height, duration_ms, camera, taken, media_done, opens_ok, $n, $n
            FROM files WHERE id = $src
            ON CONFLICT(disk_id, path) DO UPDATE SET
                size = excluded.size, mtime = excluded.mtime, ext = excluded.ext, media_type = excluded.media_type,
                quick_hash = excluded.quick_hash, full_hash = excluded.full_hash, sha256 = excluded.sha256,
                width = excluded.width, height = excluded.height, duration_ms = excluded.duration_ms,
                camera = excluded.camera, taken = excluded.taken, media_done = excluded.media_done,
                opens_ok = excluded.opens_ok, error = NULL, missing = 0, last_seen = excluded.last_seen
            """, tx, ("$dd", copy.DestDiskId), ("$dp", copy.DestPath), ("$m", copy.DestMtimeTicks), ("$q", copy.QuickHash), ("$f", copy.FullHash),
                     ("$n", copy.Stamp), ("$src", copy.SourceFileId)))
            insert.ExecuteNonQuery();
        using (var source = Cmd(c, "UPDATE files SET quick_hash = $q, full_hash = $f WHERE id = $src", tx,
                   ("$q", copy.QuickHash), ("$f", copy.FullHash), ("$src", copy.SourceFileId)))
            source.ExecuteNonQuery();
        tx.Commit();
    }, "Cannot record a copy");

    public void MarkMissing(long fileId) =>
        db.Run(c => Exec(c, "UPDATE files SET missing = 1 WHERE id = $id", ("$id", fileId)), "Cannot update the catalog");
}
