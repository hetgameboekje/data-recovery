using RecoveryBLL.Interfaces;
using static RecoveryDAL.Sqlite.SqliteDb;

namespace RecoveryDAL.Sqlite;

/// <summary>Keeps the catalog in step with Clean up (moved / removed files). Never decides anything.</summary>
public sealed class SqliteCatalogSync(SqliteDb db) : ICatalogSync
{
    public void RecordMove(long diskId, string path, long newDiskId, string newPath, long newMtimeTicks, string stamp) => db.Run(c =>
    {
        using var tx = c.BeginTransaction();
        using (var find = Cmd(c, "SELECT id FROM files WHERE disk_id = $d AND path = $p", tx, ("$d", diskId), ("$p", path)))
        {
            if (find.ExecuteScalar() is not long id) { tx.Commit(); return; }
            // An old row for the new place (a file that was there once) gives way to the moved file.
            using (var drop = Cmd(c, "DELETE FROM files WHERE disk_id = $nd AND path = $np AND id <> $id", tx,
                       ("$nd", newDiskId), ("$np", newPath), ("$id", id)))
                drop.ExecuteNonQuery();
            using (var move = Cmd(c, "UPDATE files SET disk_id = $nd, path = $np, mtime = $m, missing = 0, last_seen = $n WHERE id = $id", tx,
                       ("$nd", newDiskId), ("$np", newPath), ("$m", newMtimeTicks), ("$n", stamp), ("$id", id)))
                move.ExecuteNonQuery();
        }
        tx.Commit();
    }, "Cannot record a move in the catalog");

    public void MarkMissing(long diskId, string path) =>
        db.Run(c => Exec(c, "UPDATE files SET missing = 1 WHERE disk_id = $d AND path = $p", ("$d", diskId), ("$p", path)),
            "Cannot update the catalog");
}
