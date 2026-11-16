using System.Data;
using RecoveryBLL.Interfaces;
using static RecoveryDAL.SqlServer.SqlServerDb;

namespace RecoveryDAL.SqlServer;

/// <summary>Same as the SQLite version, in T-SQL.</summary>
public sealed class SqlServerCatalogSync(SqlServerDb db) : ICatalogSync
{
    const string Key = "CAST(HASHBYTES('SHA2_256', UPPER({0})) AS BINARY(32))";

    public void RecordMove(long diskId, string path, long newDiskId, string newPath, long newMtimeTicks, string stamp) => db.Run(c =>
    {
        using var tx = c.BeginTransaction();
        using var cmd = Cmd(c, $"""
            DECLARE @id BIGINT = (SELECT id FROM dbo.files WHERE disk_id = @d AND path_key = {string.Format(Key, "@p")});
            IF @id IS NOT NULL
            BEGIN
                DELETE FROM dbo.files WHERE disk_id = @nd AND path_key = {string.Format(Key, "@np")} AND id <> @id;
                UPDATE dbo.files SET disk_id = @nd, path = @np, mtime = @m, missing = 0, last_seen = @n WHERE id = @id;
            END
            """, tx, ("@d", diskId), ("@nd", newDiskId), ("@m", newMtimeTicks), ("@n", stamp));
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 4000).Value = path;
        cmd.Parameters.Add("@np", SqlDbType.NVarChar, 4000).Value = newPath;
        cmd.ExecuteNonQuery();
        tx.Commit();
    }, "Cannot record a move in the catalog");

    public void MarkMissing(long diskId, string path) => db.Run(c =>
    {
        using var cmd = Cmd(c, $"UPDATE dbo.files SET missing = 1 WHERE disk_id = @d AND path_key = {string.Format(Key, "@p")}", null, ("@d", diskId));
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 4000).Value = path;
        cmd.ExecuteNonQuery();
    }, "Cannot update the catalog");
}
