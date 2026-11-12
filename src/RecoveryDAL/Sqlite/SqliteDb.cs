using Microsoft.Data.Sqlite;
using RecoveryBLL.Exceptions;
using RecoveryBLL.Interfaces;

namespace RecoveryDAL.Sqlite;

/// <summary>Connection factory + schema for a SQLite catalog file. One short-lived connection per operation (pooled).</summary>
public sealed class SqliteDb : ICatalogStore
{
    public const int SchemaVersion = 1;

    // Identical to the schema of the first rtcatalog version, so existing catalog.db files keep working.
    const string Schema = """
        CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT);

        CREATE TABLE IF NOT EXISTS disks(
            id          INTEGER PRIMARY KEY,
            serial      TEXT    NOT NULL,
            total_bytes INTEGER NOT NULL,
            label       TEXT,
            fs          TEXT,
            last_root   TEXT,
            first_seen  TEXT    NOT NULL,
            last_scan   TEXT,
            UNIQUE(serial, total_bytes));

        CREATE TABLE IF NOT EXISTS files(
            id          INTEGER PRIMARY KEY,
            disk_id     INTEGER NOT NULL REFERENCES disks(id),
            path        TEXT    NOT NULL COLLATE NOCASE,
            size        INTEGER NOT NULL,
            mtime       INTEGER NOT NULL,
            ext         TEXT,
            media_type  TEXT,
            quick_hash  BLOB,
            full_hash   BLOB,
            sha256      BLOB,
            width       INTEGER,
            height      INTEGER,
            duration_ms INTEGER,
            camera      TEXT,
            taken       TEXT,
            media_done  INTEGER NOT NULL DEFAULT 0,
            opens_ok    INTEGER,
            error       TEXT,
            first_seen  TEXT    NOT NULL,
            last_seen   TEXT    NOT NULL,
            missing     INTEGER NOT NULL DEFAULT 0,
            UNIQUE(disk_id, path));

        CREATE INDEX IF NOT EXISTS ix_files_size_quick ON files(size, quick_hash);
        CREATE INDEX IF NOT EXISTS ix_files_full ON files(full_hash) WHERE full_hash IS NOT NULL;
        """;

    readonly string connectionString;
    public string FilePath { get; }
    public string Description => "SQLite: " + FilePath;
    public string? LocalFile => FilePath;

    public SqliteDb(string connectionString, bool readOnly = false)
    {
        var b = new SqliteConnectionStringBuilder(connectionString);
        FilePath = Path.GetFullPath(b.DataSource);
        b.DataSource = FilePath;
        b.Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate;
        b.Pooling = true;
        this.connectionString = b.ToString();
    }

    public SqliteConnection Open()
    {
        try
        {
            var c = new SqliteConnection(connectionString);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA busy_timeout=30000; PRAGMA foreign_keys=ON; PRAGMA synchronous=NORMAL; PRAGMA temp_store=MEMORY;";
            cmd.ExecuteNonQuery();
            return c;
        }
        catch (SqliteException ex)
        {
            throw new RepositoryException($"Cannot open {FilePath}: {ex.Message}", ex);
        }
    }

    public void EnsureCreated()
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        Run(c =>
        {
            Exec(c, "PRAGMA journal_mode=WAL;");
            Exec(c, Schema);
            Exec(c, "INSERT OR IGNORE INTO meta(key, value) VALUES('schema_version', $v)", ("$v", SchemaVersion));
        }, "Cannot create the catalog schema");
    }

    // ---- helpers shared by the SQLite repositories

    public T Run<T>(Func<SqliteConnection, T> work, string whatFailed)
    {
        try
        {
            using var c = Open();
            return work(c);
        }
        catch (SqliteException ex) { throw new RepositoryException($"{whatFailed} ({ex.SqliteErrorCode}): {ex.Message}", ex); }
    }

    public void Run(Action<SqliteConnection> work, string whatFailed) => Run(c => { work(c); return 0; }, whatFailed);

    public static SqliteCommand Cmd(SqliteConnection c, string sql, SqliteTransaction? tx = null, params (string Name, object? Value)[] ps)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        cmd.CommandTimeout = 0;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return cmd;
    }

    public static int Exec(SqliteConnection c, string sql, params (string, object?)[] ps)
    {
        using var cmd = Cmd(c, sql, null, ps);
        return cmd.ExecuteNonQuery();
    }

    public static object? Scalar(SqliteConnection c, string sql, params (string, object?)[] ps)
    {
        using var cmd = Cmd(c, sql, null, ps);
        var r = cmd.ExecuteScalar();
        return r is DBNull ? null : r;
    }

    /// <summary>"$e0,$e1,..." for an IN list, with the parameters added to the command.</summary>
    public static string InList(SqliteCommand cmd, IReadOnlyCollection<string> values, string prefix)
    {
        if (values.Count == 0) return "NULL";
        var names = new List<string>();
        int i = 0;
        foreach (var v in values)
        {
            var n = $"${prefix}{i++}";
            names.Add(n);
            cmd.Parameters.AddWithValue(n, v);
        }
        return string.Join(",", names);
    }
}
