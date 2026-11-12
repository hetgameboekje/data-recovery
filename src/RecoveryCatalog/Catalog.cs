using Microsoft.Data.Sqlite;

namespace RecoveryCatalog;

/// <summary>The fingerprint database (one SQLite file). All writes go through <see cref="Lock"/>.</summary>
sealed class Catalog : IDisposable
{
    public const int SchemaVersion = 1;

    const string Schema = """
        CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT);

        CREATE TABLE IF NOT EXISTS disks(
            id          INTEGER PRIMARY KEY,
            serial      TEXT    NOT NULL,          -- volume serial number (hex)
            total_bytes INTEGER NOT NULL,          -- volume size; together with serial identifies the disk
            label       TEXT,
            fs          TEXT,
            last_root   TEXT,                      -- drive letter it was last seen on
            first_seen  TEXT    NOT NULL,
            last_scan   TEXT,
            UNIQUE(serial, total_bytes));

        CREATE TABLE IF NOT EXISTS files(
            id          INTEGER PRIMARY KEY,
            disk_id     INTEGER NOT NULL REFERENCES disks(id),
            path        TEXT    NOT NULL COLLATE NOCASE,  -- relative to the volume root
            size        INTEGER NOT NULL,
            mtime       INTEGER NOT NULL,          -- last write time, UTC .NET ticks
            ext         TEXT,
            media_type  TEXT,                      -- Photos / Videos / Audio / Documents / Archives / Other
            quick_hash  BLOB,                      -- xxHash128 of first + last 64 KB + size (whole file if <= 128 KB)
            full_hash   BLOB,                      -- xxHash128 of the whole content
            sha256      BLOB,                      -- optional (--sha256)
            width       INTEGER,
            height      INTEGER,
            duration_ms INTEGER,
            camera      TEXT,                      -- EXIF make + model
            taken       TEXT,                      -- EXIF DateTimeOriginal, yyyy-MM-dd HH:mm:ss
            media_done  INTEGER NOT NULL DEFAULT 0,
            opens_ok    INTEGER,                   -- filled by the corrupt-file check (phase 4)
            error       TEXT,                      -- last read error, if any
            first_seen  TEXT    NOT NULL,
            last_seen   TEXT    NOT NULL,
            missing     INTEGER NOT NULL DEFAULT 0,-- 1 = gone from the disk at the last complete scan
            UNIQUE(disk_id, path));

        CREATE INDEX IF NOT EXISTS ix_files_size_quick ON files(size, quick_hash);
        CREATE INDEX IF NOT EXISTS ix_files_full ON files(full_hash) WHERE full_hash IS NOT NULL;
        """;

    public SqliteConnection Db { get; }
    public string FilePath { get; }
    public readonly object Lock = new();

    public Catalog(string path)
    {
        FilePath = Path.GetFullPath(path);
        Db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = FilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        Db.Open();
        Exec("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA temp_store=MEMORY; PRAGMA cache_size=-200000;");
        Exec(Schema);
        Exec("INSERT OR IGNORE INTO meta(key, value) VALUES('schema_version', $v)", ("$v", SchemaVersion));
    }

    public void Dispose()
    {
        try { Exec("PRAGMA optimize;"); } catch { }
        Db.Dispose();
    }

    public static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

    public SqliteCommand Cmd(string sql, SqliteTransaction? tx = null, params (string Name, object? Value)[] ps)
    {
        var c = Db.CreateCommand();
        c.CommandText = sql;
        c.Transaction = tx;
        c.CommandTimeout = 0;
        foreach (var (n, v) in ps) c.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return c;
    }

    public int Exec(string sql, params (string, object?)[] ps)
    {
        using var c = Cmd(sql, null, ps);
        return c.ExecuteNonQuery();
    }

    public object? Scalar(string sql, params (string, object?)[] ps)
    {
        using var c = Cmd(sql, null, ps);
        var r = c.ExecuteScalar();
        return r is DBNull ? null : r;
    }

    // ---------------------------------------------------------------- disks

    public long EnsureDisk(VolumeId v)
    {
        lock (Lock)
        {
            var id = Scalar("SELECT id FROM disks WHERE serial=$s AND total_bytes=$t", ("$s", v.Serial), ("$t", v.TotalBytes));
            if (id is long existing)
            {
                Exec("UPDATE disks SET label=$l, fs=$f, last_root=$r WHERE id=$id",
                    ("$l", v.Label), ("$f", v.FileSystem), ("$r", v.Root), ("$id", existing));
                return existing;
            }
            Exec("INSERT INTO disks(serial,total_bytes,label,fs,last_root,first_seen) VALUES($s,$t,$l,$f,$r,$n)",
                ("$s", v.Serial), ("$t", v.TotalBytes), ("$l", v.Label), ("$f", v.FileSystem), ("$r", v.Root), ("$n", Now()));
            return (long)Scalar("SELECT last_insert_rowid()")!;
        }
    }

    /// <summary>Catalogued disks that are plugged in right now: disk id -> current root (e.g. "G:\").</summary>
    public Dictionary<long, string> MountedRoots()
    {
        var map = new Dictionary<long, string>();
        foreach (var v in Volumes.Mounted())
        {
            var id = Scalar("SELECT id FROM disks WHERE serial=$s AND total_bytes=$t", ("$s", v.Serial), ("$t", v.TotalBytes));
            if (id is long d && !map.ContainsKey(d))
            {
                map[d] = v.Root;
                lock (Lock) Exec("UPDATE disks SET last_root=$r, label=$l WHERE id=$id", ("$r", v.Root), ("$l", v.Label), ("$id", d));
            }
        }
        return map;
    }

    public string DiskName(long id)
    {
        using var c = Cmd("SELECT label, serial, last_root FROM disks WHERE id=$id", null, ("$id", id));
        using var r = c.ExecuteReader();
        if (!r.Read()) return $"disk {id}";
        var label = r.IsDBNull(0) || r.GetString(0) == "" ? "(no label)" : r.GetString(0);
        return $"{label} [{r.GetString(1)}] {(r.IsDBNull(2) ? "" : r.GetString(2))}".TrimEnd();
    }

    // ---------------------------------------------------------------- scan

    public sealed record ScannedFile(string Path, long Size, long MtimeTicks, string Ext, string MediaType);

    /// <summary>
    /// Insert or refresh files. Hashes and media info are kept when size and modified time are unchanged,
    /// and cleared when the file changed, so it gets fingerprinted again.
    /// </summary>
    public void Upsert(long diskId, List<ScannedFile> batch, string stamp)
    {
        lock (Lock)
        {
            using var tx = Db.BeginTransaction();
            using var c = Cmd("""
                INSERT INTO files(disk_id, path, size, mtime, ext, media_type, first_seen, last_seen)
                VALUES($d, $p, $s, $m, $e, $t, $n, $n)
                ON CONFLICT(disk_id, path) DO UPDATE SET
                    ext = excluded.ext,
                    media_type = excluded.media_type,
                    last_seen = excluded.last_seen,
                    missing = 0,
                    quick_hash  = CASE WHEN files.size = excluded.size AND files.mtime = excluded.mtime THEN files.quick_hash END,
                    full_hash   = CASE WHEN files.size = excluded.size AND files.mtime = excluded.mtime THEN files.full_hash END,
                    sha256      = CASE WHEN files.size = excluded.size AND files.mtime = excluded.mtime THEN files.sha256 END,
                    width       = CASE WHEN files.size = excluded.size AND files.mtime = excluded.mtime THEN files.width END,
                    height      = CASE WHEN files.size = excluded.size AND files.mtime = excluded.mtime THEN files.height END,
                    duration_ms = CASE WHEN files.size = excluded.size AND files.mtime = excluded.mtime THEN files.duration_ms END,
                    camera      = CASE WHEN files.size = excluded.size AND files.mtime = excluded.mtime THEN files.camera END,
                    taken       = CASE WHEN files.size = excluded.size AND files.mtime = excluded.mtime THEN files.taken END,
                    opens_ok    = CASE WHEN files.size = excluded.size AND files.mtime = excluded.mtime THEN files.opens_ok END,
                    error       = CASE WHEN files.size = excluded.size AND files.mtime = excluded.mtime THEN files.error END,
                    media_done  = CASE WHEN files.size = excluded.size AND files.mtime = excluded.mtime THEN files.media_done ELSE 0 END,
                    size = excluded.size,
                    mtime = excluded.mtime
                """, tx);
            var pd = c.Parameters.Add("$d", SqliteType.Integer);
            var pp = c.Parameters.Add("$p", SqliteType.Text);
            var ps = c.Parameters.Add("$s", SqliteType.Integer);
            var pm = c.Parameters.Add("$m", SqliteType.Integer);
            var pe = c.Parameters.Add("$e", SqliteType.Text);
            var pt = c.Parameters.Add("$t", SqliteType.Text);
            var pn = c.Parameters.Add("$n", SqliteType.Text);
            c.Prepare();
            pd.Value = diskId;
            pn.Value = stamp;
            foreach (var f in batch)
            {
                pp.Value = f.Path; ps.Value = f.Size; pm.Value = f.MtimeTicks; pe.Value = f.Ext; pt.Value = f.MediaType;
                c.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    /// <summary>After a complete scan of <paramref name="prefix"/>: flag files under it that were not seen.</summary>
    public int MarkMissing(long diskId, string prefix, string stamp)
    {
        lock (Lock)
        {
            var n = prefix.Length == 0
                ? Exec("UPDATE files SET missing=1 WHERE disk_id=$d AND missing=0 AND last_seen < $n", ("$d", diskId), ("$n", stamp))
                : Exec("""
                    UPDATE files SET missing=1
                    WHERE disk_id=$d AND missing=0 AND last_seen < $n
                      AND substr(path, 1, length($p)) = $p COLLATE NOCASE
                    """, ("$d", diskId), ("$n", stamp), ("$p", prefix + "\\"));
            Exec("UPDATE disks SET last_scan=$n WHERE id=$d", ("$n", stamp), ("$d", diskId));
            return n;
        }
    }

    // ---------------------------------------------------------------- merge

    /// <summary>Merge another catalog (e.g. one made on the other PC). Newer observations win.</summary>
    public (int disks, int files) Merge(string otherPath)
    {
        otherPath = Path.GetFullPath(otherPath);
        if (string.Equals(otherPath, FilePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot merge a catalog into itself.");
        if (!File.Exists(otherPath)) throw new FileNotFoundException("Catalog not found", otherPath);

        lock (Lock)
        {
            Exec("ATTACH DATABASE $p AS other", ("$p", otherPath));
            try
            {
                using var tx = Db.BeginTransaction();
                int disks = Cmd("""
                    INSERT INTO disks(serial, total_bytes, label, fs, last_root, first_seen, last_scan)
                    SELECT serial, total_bytes, label, fs, last_root, first_seen, last_scan FROM other.disks WHERE true
                    ON CONFLICT(serial, total_bytes) DO UPDATE SET
                        last_scan = max(coalesce(disks.last_scan, ''), coalesce(excluded.last_scan, '')),
                        first_seen = min(disks.first_seen, excluded.first_seen)
                    """, tx).ExecuteNonQuery();

                int files = Cmd("""
                    INSERT INTO files(disk_id, path, size, mtime, ext, media_type, quick_hash, full_hash, sha256,
                                      width, height, duration_ms, camera, taken, media_done, opens_ok, error,
                                      first_seen, last_seen, missing)
                    SELECT d.id, o.path, o.size, o.mtime, o.ext, o.media_type, o.quick_hash, o.full_hash, o.sha256,
                           o.width, o.height, o.duration_ms, o.camera, o.taken, o.media_done, o.opens_ok, o.error,
                           o.first_seen, o.last_seen, o.missing
                    FROM other.files o
                    JOIN other.disks od ON od.id = o.disk_id
                    JOIN disks d ON d.serial = od.serial AND d.total_bytes = od.total_bytes
                    WHERE true
                    ON CONFLICT(disk_id, path) DO UPDATE SET
                        -- same file version on both sides: combine whatever each side already worked out;
                        -- otherwise the more recent observation wins.
                        quick_hash  = CASE WHEN files.size = excluded.size AND files.mtime = excluded.mtime THEN coalesce(files.quick_hash, excluded.quick_hash)
                                           WHEN excluded.last_seen > files.last_seen THEN excluded.quick_hash ELSE files.quick_hash END,
                        full_hash   = CASE WHEN files.size = excluded.size AND files.mtime = excluded.mtime THEN coalesce(files.full_hash, excluded.full_hash)
                                           WHEN excluded.last_seen > files.last_seen THEN excluded.full_hash ELSE files.full_hash END,
                        sha256      = CASE WHEN files.size = excluded.size AND files.mtime = excluded.mtime THEN coalesce(files.sha256, excluded.sha256)
                                           WHEN excluded.last_seen > files.last_seen THEN excluded.sha256 ELSE files.sha256 END,
                        width       = CASE WHEN files.size = excluded.size AND files.mtime = excluded.mtime THEN coalesce(files.width, excluded.width)
                                           WHEN excluded.last_seen > files.last_seen THEN excluded.width ELSE files.width END,
                        height      = CASE WHEN files.size = excluded.size AND files.mtime = excluded.mtime THEN coalesce(files.height, excluded.height)
                                           WHEN excluded.last_seen > files.last_seen THEN excluded.height ELSE files.height END,
                        duration_ms = CASE WHEN files.size = excluded.size AND files.mtime = excluded.mtime THEN coalesce(files.duration_ms, excluded.duration_ms)
                                           WHEN excluded.last_seen > files.last_seen THEN excluded.duration_ms ELSE files.duration_ms END,
                        camera      = CASE WHEN files.size = excluded.size AND files.mtime = excluded.mtime THEN coalesce(files.camera, excluded.camera)
                                           WHEN excluded.last_seen > files.last_seen THEN excluded.camera ELSE files.camera END,
                        taken       = CASE WHEN files.size = excluded.size AND files.mtime = excluded.mtime THEN coalesce(files.taken, excluded.taken)
                                           WHEN excluded.last_seen > files.last_seen THEN excluded.taken ELSE files.taken END,
                        media_done  = CASE WHEN files.size = excluded.size AND files.mtime = excluded.mtime THEN max(files.media_done, excluded.media_done)
                                           WHEN excluded.last_seen > files.last_seen THEN excluded.media_done ELSE files.media_done END,
                        opens_ok    = CASE WHEN excluded.last_seen > files.last_seen THEN excluded.opens_ok ELSE files.opens_ok END,
                        error       = CASE WHEN excluded.last_seen > files.last_seen THEN excluded.error ELSE files.error END,
                        ext         = CASE WHEN excluded.last_seen > files.last_seen THEN excluded.ext ELSE files.ext END,
                        media_type  = CASE WHEN excluded.last_seen > files.last_seen THEN excluded.media_type ELSE files.media_type END,
                        missing     = CASE WHEN excluded.last_seen > files.last_seen THEN excluded.missing ELSE files.missing END,
                        size        = CASE WHEN excluded.last_seen > files.last_seen THEN excluded.size ELSE files.size END,
                        mtime       = CASE WHEN excluded.last_seen > files.last_seen THEN excluded.mtime ELSE files.mtime END,
                        first_seen  = min(files.first_seen, excluded.first_seen),
                        last_seen   = max(files.last_seen, excluded.last_seen)
                    """, tx).ExecuteNonQuery();
                tx.Commit();
                return (disks, files);
            }
            finally
            {
                Exec("DETACH DATABASE other");
            }
        }
    }
}
