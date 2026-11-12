using RecoveryBLL.Dtos;
using RecoveryBLL.Exceptions;
using RecoveryBLL.Interfaces;
using static RecoveryDAL.Sqlite.SqliteDb;

namespace RecoveryDAL.Sqlite;

/// <summary>Reads a whole SQLite catalog (merge source) and merges records into one (merge target).</summary>
public sealed class SqliteCatalogExchange(SqliteDb db) : ICatalogExchange
{
    public IEnumerable<CatalogRecordDto> ReadAll()
    {
        // Streamed; SQLite errors during iteration are translated like everywhere else in the DAL.
        using var e = ReadAllCore().GetEnumerator();
        while (true)
        {
            bool more;
            try { more = e.MoveNext(); }
            catch (Microsoft.Data.Sqlite.SqliteException ex) { throw new RepositoryException($"Cannot read catalog {db.FilePath}: {ex.Message}", ex); }
            if (!more) yield break;
            yield return e.Current;
        }
    }

    IEnumerable<CatalogRecordDto> ReadAllCore()
    {
        using var c = db.Open();
        using var cmd = Cmd(c, """
            SELECT d.serial, d.total_bytes, d.label, d.fs,
                   f.path, f.size, f.mtime, f.ext, f.media_type, f.quick_hash, f.full_hash, f.sha256,
                   f.width, f.height, f.duration_ms, f.camera, f.taken, f.media_done, f.opens_ok, f.error,
                   f.first_seen, f.last_seen, f.missing
            FROM files f JOIN disks d ON d.id = f.disk_id
            ORDER BY f.disk_id, f.id
            """);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            yield return new CatalogRecordDto
            {
                DiskSerial = r.GetString(0), DiskTotalBytes = r.GetInt64(1),
                DiskLabel = r.IsDBNull(2) ? null : r.GetString(2), DiskFileSystem = r.IsDBNull(3) ? null : r.GetString(3),
                Path = r.GetString(4), Size = r.GetInt64(5), Mtime = r.GetInt64(6),
                Ext = r.IsDBNull(7) ? null : r.GetString(7), MediaType = r.IsDBNull(8) ? null : r.GetString(8),
                QuickHash = r.IsDBNull(9) ? null : (byte[])r[9], FullHash = r.IsDBNull(10) ? null : (byte[])r[10],
                Sha256 = r.IsDBNull(11) ? null : (byte[])r[11],
                Width = r.IsDBNull(12) ? null : r.GetInt32(12), Height = r.IsDBNull(13) ? null : r.GetInt32(13),
                DurationMs = r.IsDBNull(14) ? null : r.GetInt64(14),
                Camera = r.IsDBNull(15) ? null : r.GetString(15), Taken = r.IsDBNull(16) ? null : r.GetString(16),
                MediaDone = r.GetInt64(17) != 0, OpensOk = r.IsDBNull(18) ? null : r.GetInt64(18) != 0,
                Error = r.IsDBNull(19) ? null : r.GetString(19),
                FirstSeen = r.GetString(20), LastSeen = r.GetString(21), Missing = r.GetInt64(22) != 0,
            };
        }
    }

    // Same file version on both sides: combine what each side already worked out. Otherwise the newer observation wins.
    const string Same = "files.size = excluded.size AND files.mtime = excluded.mtime";
    const string Newer = "excluded.last_seen > files.last_seen";
    static string Combine(string col) => $"{col} = CASE WHEN {Same} THEN coalesce(files.{col}, excluded.{col}) WHEN {Newer} THEN excluded.{col} ELSE files.{col} END";
    static string Latest(string col) => $"{col} = CASE WHEN {Newer} THEN excluded.{col} ELSE files.{col} END";

    public int MergeRecords(IReadOnlyList<CatalogRecordDto> records) => db.Run(c =>
    {
        using var tx = c.BeginTransaction();
        using var cmd = Cmd(c, $"""
            INSERT INTO files(disk_id, path, size, mtime, ext, media_type, quick_hash, full_hash, sha256, width, height, duration_ms,
                              camera, taken, media_done, opens_ok, error, first_seen, last_seen, missing)
            VALUES($d, $p, $s, $m, $e, $t, $q, $f, $h, $w, $hh, $du, $c, $tk, $md, $ok, $er, $fs, $ls, $mi)
            ON CONFLICT(disk_id, path) DO UPDATE SET
                {Combine("quick_hash")}, {Combine("full_hash")}, {Combine("sha256")},
                {Combine("width")}, {Combine("height")}, {Combine("duration_ms")}, {Combine("camera")}, {Combine("taken")},
                media_done = CASE WHEN {Same} THEN max(files.media_done, excluded.media_done) WHEN {Newer} THEN excluded.media_done ELSE files.media_done END,
                {Latest("opens_ok")}, {Latest("error")}, {Latest("ext")}, {Latest("media_type")}, {Latest("missing")},
                {Latest("size")}, {Latest("mtime")},
                first_seen = min(files.first_seen, excluded.first_seen),
                last_seen = max(files.last_seen, excluded.last_seen)
            """, tx);
        int n = 0;
        foreach (var r in records)
        {
            if (r.DiskId <= 0) throw new RepositoryException("Merge record without target disk id");
            cmd.Parameters.Clear();
            foreach (var (name, value) in new (string, object?)[]
                     {
                         ("$d", r.DiskId), ("$p", r.Path), ("$s", r.Size), ("$m", r.Mtime), ("$e", r.Ext), ("$t", r.MediaType),
                         ("$q", r.QuickHash), ("$f", r.FullHash), ("$h", r.Sha256), ("$w", r.Width), ("$hh", r.Height),
                         ("$du", r.DurationMs), ("$c", r.Camera), ("$tk", r.Taken), ("$md", r.MediaDone ? 1 : 0),
                         ("$ok", r.OpensOk is { } ok ? (ok ? 1 : 0) : null), ("$er", r.Error),
                         ("$fs", r.FirstSeen), ("$ls", r.LastSeen), ("$mi", r.Missing ? 1 : 0),
                     })
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            n += cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return n;
    }, "Cannot merge records");
}

/// <summary>Opens another catalog file (always SQLite: that is what rtcatalog writes on other PCs) as a merge source.</summary>
public sealed class SqliteCatalogSourceFactory(RecoveryBLL.Models.AppSettings settings) : ICatalogSourceFactory
{
    public ICatalogExchange OpenCatalogFile(string path)
    {
        var full = Path.GetFullPath(path);
        if (settings.Provider == RecoveryBLL.Models.DatabaseProvider.Sqlite &&
            string.Equals(new SqliteDb(settings.ConnectionString).FilePath, full, StringComparison.OrdinalIgnoreCase))
            throw new RepositoryException("Cannot merge a catalog into itself.");
        return new SqliteCatalogExchange(new SqliteDb($"Data Source={full}", readOnly: true));
    }
}
