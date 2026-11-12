using System.Data;
using Microsoft.Data.SqlClient;
using RecoveryBLL.Dtos;
using RecoveryBLL.Exceptions;
using RecoveryBLL.Interfaces;
using static RecoveryDAL.SqlServer.SqlServerDb;

namespace RecoveryDAL.SqlServer;

/// <summary>Merge target on SQL Server (records staged with SqlBulkCopy, then one MERGE). Also exports its rows.</summary>
public sealed class SqlServerCatalogExchange(SqlServerDb db) : ICatalogExchange
{
    public IEnumerable<CatalogRecordDto> ReadAll()
    {
        // Small enough per call in practice; materialised so the connection closes before the caller iterates.
        return db.Run(c =>
        {
            using var cmd = Cmd(c, """
                SELECT d.serial, d.total_bytes, d.label, d.fs, f.path, f.size, f.mtime, f.ext, f.media_type,
                       f.quick_hash, f.full_hash, f.sha256, f.width, f.height, f.duration_ms, f.camera, f.taken,
                       f.media_done, f.opens_ok, f.error, f.first_seen, f.last_seen, f.missing
                FROM dbo.files f JOIN dbo.disks d ON d.id = f.disk_id ORDER BY f.disk_id, f.id
                """);
            var list = new List<CatalogRecordDto>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new CatalogRecordDto
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
                    MediaDone = r.GetBoolean(17), OpensOk = r.IsDBNull(18) ? null : r.GetBoolean(18),
                    Error = r.IsDBNull(19) ? null : r.GetString(19),
                    FirstSeen = r.GetString(20), LastSeen = r.GetString(21), Missing = r.GetBoolean(22),
                });
            return list;
        }, "Cannot read the catalog");
    }

    const string Same = "t.size = s.size AND t.mtime = s.mtime";
    const string Newer = "s.last_seen > t.last_seen";
    static string Combine(string col) => $"{col} = CASE WHEN {Same} THEN COALESCE(t.{col}, s.{col}) WHEN {Newer} THEN s.{col} ELSE t.{col} END";
    static string Latest(string col) => $"{col} = CASE WHEN {Newer} THEN s.{col} ELSE t.{col} END";

    public int MergeRecords(IReadOnlyList<CatalogRecordDto> records) => db.Run(c =>
    {
        using var tx = c.BeginTransaction();
        using (var create = Cmd(c, """
            CREATE TABLE #merge(disk_id BIGINT NOT NULL, path NVARCHAR(4000) NOT NULL, size BIGINT NOT NULL, mtime BIGINT NOT NULL,
                ext NVARCHAR(32) NULL, media_type NVARCHAR(32) NULL, quick_hash VARBINARY(16) NULL, full_hash VARBINARY(16) NULL,
                sha256 VARBINARY(32) NULL, width INT NULL, height INT NULL, duration_ms BIGINT NULL, camera NVARCHAR(400) NULL,
                taken VARCHAR(19) NULL, media_done BIT NOT NULL, opens_ok BIT NULL, error NVARCHAR(MAX) NULL,
                first_seen VARCHAR(24) NOT NULL, last_seen VARCHAR(24) NOT NULL, missing BIT NOT NULL)
            """, tx)) create.ExecuteNonQuery();

        var t = new DataTable();
        foreach (var (name, type) in new (string, Type)[]
                 {
                     ("disk_id", typeof(long)), ("path", typeof(string)), ("size", typeof(long)), ("mtime", typeof(long)),
                     ("ext", typeof(string)), ("media_type", typeof(string)), ("quick_hash", typeof(byte[])), ("full_hash", typeof(byte[])),
                     ("sha256", typeof(byte[])), ("width", typeof(int)), ("height", typeof(int)), ("duration_ms", typeof(long)),
                     ("camera", typeof(string)), ("taken", typeof(string)), ("media_done", typeof(bool)), ("opens_ok", typeof(bool)),
                     ("error", typeof(string)), ("first_seen", typeof(string)), ("last_seen", typeof(string)), ("missing", typeof(bool)),
                 })
            t.Columns.Add(name, type);
        foreach (var r in records)
        {
            if (r.DiskId <= 0) throw new RepositoryException("Merge record without target disk id");
            t.Rows.Add(r.DiskId, r.Path, r.Size, r.Mtime, Db(r.Ext), Db(r.MediaType), Db(r.QuickHash), Db(r.FullHash), Db(r.Sha256),
                Db(r.Width), Db(r.Height), Db(r.DurationMs), Db(r.Camera), Db(r.Taken), r.MediaDone, Db(r.OpensOk), Db(r.Error),
                r.FirstSeen, r.LastSeen, r.Missing);
        }
        BulkCopy(c, tx, "#merge", t);

        int n;
        using (var merge = Cmd(c, $"""
            MERGE dbo.files WITH (HOLDLOCK) AS t
            USING (SELECT m.*, CAST(HASHBYTES('SHA2_256', UPPER(m.path)) AS BINARY(32)) AS path_key FROM #merge m) AS s
            ON t.disk_id = s.disk_id AND t.path_key = s.path_key
            WHEN MATCHED THEN UPDATE SET
                {Combine("quick_hash")}, {Combine("full_hash")}, {Combine("sha256")},
                {Combine("width")}, {Combine("height")}, {Combine("duration_ms")}, {Combine("camera")}, {Combine("taken")},
                media_done = CASE WHEN {Same} THEN (CASE WHEN t.media_done = 1 OR s.media_done = 1 THEN 1 ELSE 0 END)
                                  WHEN {Newer} THEN s.media_done ELSE t.media_done END,
                {Latest("opens_ok")}, {Latest("error")}, {Latest("ext")}, {Latest("media_type")}, {Latest("missing")},
                {Latest("size")}, {Latest("mtime")},
                first_seen = CASE WHEN s.first_seen < t.first_seen THEN s.first_seen ELSE t.first_seen END,
                last_seen  = CASE WHEN s.last_seen  > t.last_seen  THEN s.last_seen  ELSE t.last_seen  END
            WHEN NOT MATCHED THEN
                INSERT(disk_id, path, size, mtime, ext, media_type, quick_hash, full_hash, sha256, width, height, duration_ms,
                       camera, taken, media_done, opens_ok, error, first_seen, last_seen, missing)
                VALUES(s.disk_id, s.path, s.size, s.mtime, s.ext, s.media_type, s.quick_hash, s.full_hash, s.sha256, s.width,
                       s.height, s.duration_ms, s.camera, s.taken, s.media_done, s.opens_ok, s.error, s.first_seen, s.last_seen, s.missing);
            """, tx)) n = merge.ExecuteNonQuery();
        using (var drop = Cmd(c, "DROP TABLE #merge", tx)) drop.ExecuteNonQuery();
        tx.Commit();
        return n;
    }, "Cannot merge records");
}
