using System.Data;
using Microsoft.Data.SqlClient;
using RecoveryBLL.Exceptions;
using RecoveryBLL.Interfaces;

namespace RecoveryDAL.SqlServer;

/// <summary>
/// Connection factory + schema for SQL Server (LocalDB, Express or a full server shared by several PCs).
/// Paths can be long, so uniqueness uses a persisted SHA-256 of the upper-cased path (path_key).
/// </summary>
public sealed class SqlServerDb(string connectionString) : ICatalogStore
{
    const string Schema = """
        IF OBJECT_ID(N'dbo.meta') IS NULL
            CREATE TABLE dbo.meta([key] NVARCHAR(100) NOT NULL PRIMARY KEY, value NVARCHAR(400) NULL);

        IF OBJECT_ID(N'dbo.disks') IS NULL
            CREATE TABLE dbo.disks(
                id          BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                serial      NVARCHAR(64)  NOT NULL,
                total_bytes BIGINT        NOT NULL,
                label       NVARCHAR(260) NULL,
                fs          NVARCHAR(32)  NULL,
                last_root   NVARCHAR(400) NULL,
                first_seen  VARCHAR(24)   NOT NULL,
                last_scan   VARCHAR(24)   NULL,
                CONSTRAINT UQ_disks_identity UNIQUE(serial, total_bytes));

        IF OBJECT_ID(N'dbo.files') IS NULL
            CREATE TABLE dbo.files(
                id          BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                disk_id     BIGINT         NOT NULL REFERENCES dbo.disks(id),
                path        NVARCHAR(4000) NOT NULL,
                path_key    AS CAST(HASHBYTES('SHA2_256', UPPER(path)) AS BINARY(32)) PERSISTED,
                size        BIGINT         NOT NULL,
                mtime       BIGINT         NOT NULL,
                ext         NVARCHAR(32)   NULL,
                media_type  NVARCHAR(32)   NULL,
                quick_hash  VARBINARY(16)  NULL,
                full_hash   VARBINARY(16)  NULL,
                sha256      VARBINARY(32)  NULL,
                width       INT            NULL,
                height      INT            NULL,
                duration_ms BIGINT         NULL,
                camera      NVARCHAR(400)  NULL,
                taken       VARCHAR(19)    NULL,
                media_done  BIT            NOT NULL DEFAULT 0,
                opens_ok    BIT            NULL,
                error       NVARCHAR(MAX)  NULL,
                first_seen  VARCHAR(24)    NOT NULL,
                last_seen   VARCHAR(24)    NOT NULL,
                missing     BIT            NOT NULL DEFAULT 0,
                CONSTRAINT UQ_files_path UNIQUE(disk_id, path_key));

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_files_size_quick' AND object_id = OBJECT_ID(N'dbo.files'))
            CREATE INDEX ix_files_size_quick ON dbo.files(size, quick_hash);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_files_full' AND object_id = OBJECT_ID(N'dbo.files'))
            CREATE INDEX ix_files_full ON dbo.files(full_hash) WHERE full_hash IS NOT NULL;

        IF NOT EXISTS (SELECT 1 FROM dbo.meta WHERE [key] = 'schema_version')
            INSERT INTO dbo.meta([key], value) VALUES('schema_version', '1');
        """;

    public string Description => Configuration.EnvSettingsLoader.Describe(RecoveryBLL.Models.DatabaseProvider.SqlServer, connectionString);

    public SqlConnection Open()
    {
        try
        {
            var c = new SqlConnection(connectionString);
            c.Open();
            return c;
        }
        catch (SqlException ex) { throw new RepositoryException($"Cannot connect to {Description}: {ex.Message}", ex); }
    }

    public void EnsureCreated()
    {
        var b = new SqlConnectionStringBuilder(connectionString);
        var dbName = b.InitialCatalog;
        if (string.IsNullOrWhiteSpace(dbName)) throw new RepositoryException("DB_CONNECTION needs a Database=... name");
        try
        {
            // Create the database itself if it does not exist yet (needs CREATE DATABASE rights; LocalDB has them).
            b.InitialCatalog = "master";
            using (var master = new SqlConnection(b.ConnectionString))
            {
                master.Open();
                using var cmd = master.CreateCommand();
                cmd.CommandText = "IF DB_ID(@n) IS NULL BEGIN DECLARE @sql NVARCHAR(400) = N'CREATE DATABASE ' + QUOTENAME(@n); EXEC(@sql); END";
                cmd.Parameters.AddWithValue("@n", dbName);
                cmd.ExecuteNonQuery();
            }
            using var c = Open();
            Exec(c, Schema);
        }
        catch (SqlException ex) { throw new RepositoryException($"Cannot create the catalog schema on {Description}: {ex.Message}", ex); }
    }

    // ---- helpers shared by the SQL Server repositories

    public T Run<T>(Func<SqlConnection, T> work, string whatFailed)
    {
        try
        {
            using var c = Open();
            return work(c);
        }
        catch (SqlException ex) { throw new RepositoryException($"{whatFailed}: {ex.Message}", ex); }
    }

    public void Run(Action<SqlConnection> work, string whatFailed) => Run(c => { work(c); return 0; }, whatFailed);

    public static SqlCommand Cmd(SqlConnection c, string sql, SqlTransaction? tx = null, params (string Name, object? Value)[] ps)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        cmd.CommandTimeout = 0;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return cmd;
    }

    public static int Exec(SqlConnection c, string sql, params (string, object?)[] ps)
    {
        using var cmd = Cmd(c, sql, null, ps);
        return cmd.ExecuteNonQuery();
    }

    public static void BulkCopy(SqlConnection c, SqlTransaction tx, string table, DataTable data)
    {
        using var bulk = new SqlBulkCopy(c, SqlBulkCopyOptions.Default, tx) { DestinationTableName = table, BatchSize = 5000, BulkCopyTimeout = 0 };
        foreach (DataColumn col in data.Columns) bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
        bulk.WriteToServer(data);
    }

    public static object Db(object? v) => v ?? DBNull.Value;
}
