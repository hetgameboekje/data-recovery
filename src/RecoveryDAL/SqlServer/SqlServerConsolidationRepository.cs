using System.Data;
using RecoveryBLL.Dtos;
using RecoveryBLL.Interfaces;
using RecoveryDAL.Sqlite;
using static RecoveryDAL.SqlServer.SqlServerDb;

namespace RecoveryDAL.SqlServer;

/// <summary>Same as the SQLite version, in T-SQL.</summary>
public sealed class SqlServerConsolidationRepository(SqlServerDb db) : IConsolidationRepository
{
    public IReadOnlyList<PlanFileDto> GetPlanFiles() => db.Run(c =>
    {
        using var cmd = Cmd(c, SqlServerReportRepository.Classified + """
            SELECT f.id, f.disk_id, f.path, f.size, f.mtime, f.media_type, f.full_hash, k.kind
            FROM k JOIN dbo.files f ON f.id = k.id
            """);
        var list = new List<PlanFileDto>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PlanFileDto(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt64(3), r.GetInt64(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : (byte[])r[6], SqliteConsolidationRepository.Kind(r.GetString(7))));
        return list;
    }, "Cannot read the catalog for the copy plan");

    public void RecordCopy(CopyRecordDto copy) => db.Run(c =>
    {
        using var tx = c.BeginTransaction();
        using var cmd = Cmd(c, """
            MERGE dbo.files WITH (HOLDLOCK) AS t
            USING (SELECT @dd AS disk_id, @dp AS path, CAST(HASHBYTES('SHA2_256', UPPER(@dp)) AS BINARY(32)) AS path_key,
                          size, @m AS mtime, ext, media_type, sha256, width, height, duration_ms, camera, taken, media_done, opens_ok
                   FROM dbo.files WHERE id = @src) AS s
            ON t.disk_id = s.disk_id AND t.path_key = s.path_key
            WHEN MATCHED THEN UPDATE SET
                size = s.size, mtime = s.mtime, ext = s.ext, media_type = s.media_type,
                quick_hash = @q, full_hash = @f, sha256 = s.sha256, width = s.width, height = s.height,
                duration_ms = s.duration_ms, camera = s.camera, taken = s.taken, media_done = s.media_done,
                opens_ok = s.opens_ok, error = NULL, missing = 0, last_seen = @n
            WHEN NOT MATCHED THEN
                INSERT(disk_id, path, size, mtime, ext, media_type, quick_hash, full_hash, sha256,
                       width, height, duration_ms, camera, taken, media_done, opens_ok, first_seen, last_seen)
                VALUES(s.disk_id, s.path, s.size, s.mtime, s.ext, s.media_type, @q, @f, s.sha256,
                       s.width, s.height, s.duration_ms, s.camera, s.taken, s.media_done, s.opens_ok, @n, @n);
            UPDATE dbo.files SET quick_hash = @q, full_hash = @f WHERE id = @src;
            """, tx, ("@dd", copy.DestDiskId), ("@m", copy.DestMtimeTicks), ("@n", copy.Stamp), ("@src", copy.SourceFileId));
        cmd.Parameters.Add("@dp", SqlDbType.NVarChar, 4000).Value = copy.DestPath;
        cmd.Parameters.Add("@q", SqlDbType.VarBinary, 16).Value = copy.QuickHash;
        cmd.Parameters.Add("@f", SqlDbType.VarBinary, 16).Value = copy.FullHash;
        cmd.ExecuteNonQuery();
        tx.Commit();
    }, "Cannot record a copy");

    public void MarkMissing(long fileId) =>
        db.Run(c => Exec(c, "UPDATE dbo.files SET missing = 1 WHERE id = @id", ("@id", fileId)), "Cannot update the catalog");
}
