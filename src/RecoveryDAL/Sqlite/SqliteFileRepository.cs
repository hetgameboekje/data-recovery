using Microsoft.Data.Sqlite;
using RecoveryBLL.Dtos;
using RecoveryBLL.Interfaces;
using static RecoveryDAL.Sqlite.SqliteDb;

namespace RecoveryDAL.Sqlite;

public sealed class SqliteFileRepository(SqliteDb db) : IFileRepository
{
    const string Unchanged = "files.size = excluded.size AND files.mtime = excluded.mtime";

    public void UpsertScanned(long diskId, IReadOnlyList<ScannedFileDto> batch, string stamp) => db.Run(c =>
    {
        using var tx = c.BeginTransaction();
        using var cmd = Cmd(c, $"""
            INSERT INTO files(disk_id, path, size, mtime, ext, media_type, first_seen, last_seen)
            VALUES($d, $p, $s, $m, $e, $t, $n, $n)
            ON CONFLICT(disk_id, path) DO UPDATE SET
                ext = excluded.ext,
                media_type = excluded.media_type,
                last_seen = excluded.last_seen,
                missing = 0,
                quick_hash  = CASE WHEN {Unchanged} THEN files.quick_hash END,
                full_hash   = CASE WHEN {Unchanged} THEN files.full_hash END,
                sha256      = CASE WHEN {Unchanged} THEN files.sha256 END,
                width       = CASE WHEN {Unchanged} THEN files.width END,
                height      = CASE WHEN {Unchanged} THEN files.height END,
                duration_ms = CASE WHEN {Unchanged} THEN files.duration_ms END,
                camera      = CASE WHEN {Unchanged} THEN files.camera END,
                taken       = CASE WHEN {Unchanged} THEN files.taken END,
                opens_ok    = CASE WHEN {Unchanged} THEN files.opens_ok END,
                error       = CASE WHEN {Unchanged} THEN files.error END,
                media_done  = CASE WHEN {Unchanged} THEN files.media_done ELSE 0 END,
                size = excluded.size,
                mtime = excluded.mtime
            """, tx);
        var pp = cmd.Parameters.Add("$p", SqliteType.Text);
        var ps = cmd.Parameters.Add("$s", SqliteType.Integer);
        var pm = cmd.Parameters.Add("$m", SqliteType.Integer);
        var pe = cmd.Parameters.Add("$e", SqliteType.Text);
        var pt = cmd.Parameters.Add("$t", SqliteType.Text);
        cmd.Parameters.AddWithValue("$d", diskId);
        cmd.Parameters.AddWithValue("$n", stamp);
        cmd.Prepare();
        foreach (var f in batch)
        {
            pp.Value = f.Path; ps.Value = f.Size; pm.Value = f.MtimeTicks; pe.Value = f.Ext; pt.Value = f.MediaType;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }, "Cannot save scanned files");

    public int MarkMissingNotSeen(long diskId, string pathPrefix, string stamp) => db.Run(c => pathPrefix.Length == 0
        ? Exec(c, "UPDATE files SET missing=1 WHERE disk_id=$d AND missing=0 AND last_seen < $n", ("$d", diskId), ("$n", stamp))
        : Exec(c, """
            UPDATE files SET missing=1
            WHERE disk_id=$d AND missing=0 AND last_seen < $n
              AND substr(path, 1, length($p)) = $p COLLATE NOCASE
            """, ("$d", diskId), ("$n", stamp), ("$p", pathPrefix + "\\")), "Cannot flag missing files");

    public IReadOnlyList<FileJobDto> GetJobs(long diskId, FingerprintStage stage, bool includeSha256, IReadOnlyCollection<string> imageExtensions) => db.Run(c =>
    {
        using var cmd = c.CreateCommand();
        cmd.CommandTimeout = 0;
        cmd.Parameters.AddWithValue("$d", diskId);
        cmd.CommandText = stage switch
        {
            // Funnel step 1: only files that share their size with another catalogued file.
            FingerprintStage.QuickHash => """
                SELECT f.id, f.path, f.size, f.ext FROM files f
                WHERE f.disk_id = $d AND f.missing = 0 AND f.size > 0 AND f.quick_hash IS NULL AND f.error IS NULL
                  AND EXISTS (SELECT 1 FROM files g WHERE g.size = f.size AND g.id <> f.id AND g.missing = 0)
                ORDER BY f.path
                """,
            // Funnel step 2: only files whose size AND quick hash match another file.
            FingerprintStage.FullHash => $"""
                SELECT f.id, f.path, f.size, f.ext FROM files f
                WHERE f.disk_id = $d AND f.missing = 0 AND f.error IS NULL AND f.quick_hash IS NOT NULL
                  AND {(includeSha256 ? "(f.full_hash IS NULL OR f.sha256 IS NULL)" : "f.full_hash IS NULL")}
                  AND EXISTS (SELECT 1 FROM files g WHERE g.size = f.size AND g.quick_hash = f.quick_hash AND g.id <> f.id AND g.missing = 0)
                ORDER BY f.path
                """,
            FingerprintStage.ImageInfo => "",
            _ => throw new ArgumentOutOfRangeException(nameof(stage)),
        };
        if (stage == FingerprintStage.ImageInfo)
            cmd.CommandText = $"""
                SELECT f.id, f.path, f.size, f.ext FROM files f
                WHERE f.disk_id = $d AND f.missing = 0 AND f.error IS NULL AND f.media_done = 0
                  AND f.ext IN ({InList(cmd, imageExtensions, "e")})
                ORDER BY f.path
                """;
        var list = new List<FileJobDto>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new FileJobDto(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.IsDBNull(3) ? "" : r.GetString(3)));
        return list;
    }, "Cannot list files to fingerprint");

    public void SaveResults(IReadOnlyList<FingerprintResultDto> results) => db.Run(c =>
    {
        using var tx = c.BeginTransaction();
        using var quick = Cmd(c, "UPDATE files SET quick_hash=$q, full_hash=coalesce($f, full_hash), sha256=coalesce($s, sha256) WHERE id=$id", tx);
        using var full = Cmd(c, "UPDATE files SET full_hash=$f, sha256=coalesce($s, sha256) WHERE id=$id", tx);
        using var media = Cmd(c, "UPDATE files SET width=$w, height=$h, camera=$c, taken=$t, media_done=1 WHERE id=$id", tx);
        using var error = Cmd(c, "UPDATE files SET error=$e WHERE id=$id", tx);
        using var missing = Cmd(c, "UPDATE files SET missing=1 WHERE id=$id", tx);
        foreach (var r in results)
        {
            var cmd = r.Kind switch
            {
                FingerprintResultKind.QuickHash => Set(quick, ("$q", r.QuickHash), ("$f", r.FullHash), ("$s", r.Sha256)),
                FingerprintResultKind.FullHash => Set(full, ("$f", r.FullHash), ("$s", r.Sha256)),
                FingerprintResultKind.Media => Set(media, ("$w", r.Width), ("$h", r.Height), ("$c", r.Camera), ("$t", r.Taken)),
                FingerprintResultKind.Error => Set(error, ("$e", r.Error)),
                _ => Set(missing),
            };
            cmd.Parameters.AddWithValue("$id", r.Id);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }, "Cannot save fingerprints");

    static SqliteCommand Set(SqliteCommand cmd, params (string Name, object? Value)[] ps)
    {
        cmd.Parameters.Clear();
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return cmd;
    }
}
