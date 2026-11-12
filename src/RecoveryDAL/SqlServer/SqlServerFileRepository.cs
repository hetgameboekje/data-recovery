using System.Data;
using Microsoft.Data.SqlClient;
using RecoveryBLL.Dtos;
using RecoveryBLL.Interfaces;
using static RecoveryDAL.SqlServer.SqlServerDb;

namespace RecoveryDAL.SqlServer;

public sealed class SqlServerFileRepository(SqlServerDb db) : IFileRepository
{
    const string Unchanged = "t.size = s.size AND t.mtime = s.mtime";

    public void UpsertScanned(long diskId, IReadOnlyList<ScannedFileDto> batch, string stamp) => db.Run(c =>
    {
        using var tx = c.BeginTransaction();
        using (var create = Cmd(c, """
            CREATE TABLE #scan(path NVARCHAR(4000) NOT NULL, size BIGINT NOT NULL, mtime BIGINT NOT NULL,
                               ext NVARCHAR(32) NULL, media_type NVARCHAR(32) NULL)
            """, tx)) create.ExecuteNonQuery();

        var table = new DataTable();
        table.Columns.Add("path", typeof(string));
        table.Columns.Add("size", typeof(long));
        table.Columns.Add("mtime", typeof(long));
        table.Columns.Add("ext", typeof(string));
        table.Columns.Add("media_type", typeof(string));
        foreach (var f in batch) table.Rows.Add(f.Path, f.Size, f.MtimeTicks, f.Ext, f.MediaType);
        BulkCopy(c, tx, "#scan", table);

        using (var merge = Cmd(c, $"""
            MERGE dbo.files WITH (HOLDLOCK) AS t
            USING (SELECT @d AS disk_id, path, CAST(HASHBYTES('SHA2_256', UPPER(path)) AS BINARY(32)) AS path_key,
                          size, mtime, ext, media_type FROM #scan) AS s
            ON t.disk_id = s.disk_id AND t.path_key = s.path_key
            WHEN MATCHED THEN UPDATE SET
                ext = s.ext, media_type = s.media_type, last_seen = @n, missing = 0,
                quick_hash  = CASE WHEN {Unchanged} THEN t.quick_hash END,
                full_hash   = CASE WHEN {Unchanged} THEN t.full_hash END,
                sha256      = CASE WHEN {Unchanged} THEN t.sha256 END,
                width       = CASE WHEN {Unchanged} THEN t.width END,
                height      = CASE WHEN {Unchanged} THEN t.height END,
                duration_ms = CASE WHEN {Unchanged} THEN t.duration_ms END,
                camera      = CASE WHEN {Unchanged} THEN t.camera END,
                taken       = CASE WHEN {Unchanged} THEN t.taken END,
                opens_ok    = CASE WHEN {Unchanged} THEN t.opens_ok END,
                error       = CASE WHEN {Unchanged} THEN t.error END,
                media_done  = CASE WHEN {Unchanged} THEN t.media_done ELSE 0 END,
                size = s.size, mtime = s.mtime
            WHEN NOT MATCHED THEN
                INSERT(disk_id, path, size, mtime, ext, media_type, first_seen, last_seen)
                VALUES(s.disk_id, s.path, s.size, s.mtime, s.ext, s.media_type, @n, @n);
            DROP TABLE #scan;
            """, tx, ("@d", diskId), ("@n", stamp))) merge.ExecuteNonQuery();
        tx.Commit();
    }, "Cannot save scanned files");

    public int MarkMissingNotSeen(long diskId, string pathPrefix, string stamp) => db.Run(c => pathPrefix.Length == 0
        ? Exec(c, "UPDATE dbo.files SET missing=1 WHERE disk_id=@d AND missing=0 AND last_seen < @n", ("@d", diskId), ("@n", stamp))
        : Exec(c, """
            UPDATE dbo.files SET missing=1
            WHERE disk_id=@d AND missing=0 AND last_seen < @n
              AND UPPER(LEFT(path, LEN(@p))) = UPPER(@p)
            """, ("@d", diskId), ("@n", stamp), ("@p", pathPrefix + "\\")), "Cannot flag missing files");

    public IReadOnlyList<FileJobDto> GetJobs(long diskId, FingerprintStage stage, bool includeSha256, IReadOnlyCollection<string> imageExtensions) => db.Run(c =>
    {
        using var cmd = Cmd(c, "", null, ("@d", diskId));
        cmd.CommandText = stage switch
        {
            FingerprintStage.QuickHash => """
                SELECT f.id, f.path, f.size, f.ext FROM dbo.files f
                WHERE f.disk_id = @d AND f.missing = 0 AND f.size > 0 AND f.quick_hash IS NULL AND f.error IS NULL
                  AND EXISTS (SELECT 1 FROM dbo.files g WHERE g.size = f.size AND g.id <> f.id AND g.missing = 0)
                ORDER BY f.path
                """,
            FingerprintStage.FullHash => $"""
                SELECT f.id, f.path, f.size, f.ext FROM dbo.files f
                WHERE f.disk_id = @d AND f.missing = 0 AND f.error IS NULL AND f.quick_hash IS NOT NULL
                  AND {(includeSha256 ? "(f.full_hash IS NULL OR f.sha256 IS NULL)" : "f.full_hash IS NULL")}
                  AND EXISTS (SELECT 1 FROM dbo.files g WHERE g.size = f.size AND g.quick_hash = f.quick_hash AND g.id <> f.id AND g.missing = 0)
                ORDER BY f.path
                """,
            FingerprintStage.ImageInfo => $"""
                SELECT f.id, f.path, f.size, f.ext FROM dbo.files f
                WHERE f.disk_id = @d AND f.missing = 0 AND f.error IS NULL AND f.media_done = 0
                  AND f.ext IN ({InList(cmd, imageExtensions)})
                ORDER BY f.path
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(stage)),
        };
        var list = new List<FileJobDto>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new FileJobDto(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.IsDBNull(3) ? "" : r.GetString(3)));
        return list;
    }, "Cannot list files to fingerprint");

    public void SaveResults(IReadOnlyList<FingerprintResultDto> results) => db.Run(c =>
    {
        using var tx = c.BeginTransaction();
        foreach (var r in results)
        {
            using var cmd = r.Kind switch
            {
                FingerprintResultKind.QuickHash => Cmd(c, "UPDATE dbo.files SET quick_hash=@q, full_hash=COALESCE(@f, full_hash), sha256=COALESCE(@s, sha256) WHERE id=@id",
                    tx, ("@q", r.QuickHash), ("@f", r.FullHash), ("@s", r.Sha256)),
                FingerprintResultKind.FullHash => Cmd(c, "UPDATE dbo.files SET full_hash=@f, sha256=COALESCE(@s, sha256) WHERE id=@id",
                    tx, ("@f", r.FullHash), ("@s", r.Sha256)),
                FingerprintResultKind.Media => Cmd(c, "UPDATE dbo.files SET width=@w, height=@h, camera=@c, taken=@t, media_done=1 WHERE id=@id",
                    tx, ("@w", r.Width), ("@h", r.Height), ("@c", r.Camera), ("@t", r.Taken)),
                FingerprintResultKind.Error => Cmd(c, "UPDATE dbo.files SET error=@e WHERE id=@id", tx, ("@e", r.Error)),
                _ => Cmd(c, "UPDATE dbo.files SET missing=1 WHERE id=@id", tx),
            };
            // varbinary parameters must be typed explicitly, or a NULL is sent as nvarchar
            foreach (SqlParameter p in cmd.Parameters)
                if (p.ParameterName is "@q" or "@f" or "@s") p.SqlDbType = SqlDbType.VarBinary;
            cmd.Parameters.AddWithValue("@id", r.Id);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }, "Cannot save fingerprints");

    static string InList(SqlCommand cmd, IReadOnlyCollection<string> values)
    {
        if (values.Count == 0) return "NULL";
        var names = new List<string>();
        int i = 0;
        foreach (var v in values)
        {
            var n = $"@e{i++}";
            names.Add(n);
            cmd.Parameters.AddWithValue(n, v);
        }
        return string.Join(",", names);
    }
}
