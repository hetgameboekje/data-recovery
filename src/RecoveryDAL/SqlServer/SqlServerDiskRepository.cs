using Microsoft.Data.SqlClient;
using RecoveryBLL.Dtos;
using RecoveryBLL.Interfaces;
using static RecoveryDAL.SqlServer.SqlServerDb;

namespace RecoveryDAL.SqlServer;

public sealed class SqlServerDiskRepository(SqlServerDb db) : IDiskRepository
{
    const string Select = """
        SELECT d.id, d.serial, d.total_bytes, d.label, d.fs, d.last_root, d.first_seen, d.last_scan,
               (SELECT COUNT_BIG(*) FROM dbo.files f WHERE f.disk_id = d.id AND f.missing = 0)
        FROM dbo.disks d
        """;

    public IReadOnlyList<DiskDto> GetAll() => db.Run(c =>
    {
        using var cmd = Cmd(c, Select + " ORDER BY d.id");
        return Read(cmd);
    }, "Cannot read disks");

    public DiskDto? GetById(long id) => db.Run(c =>
    {
        using var cmd = Cmd(c, Select + " WHERE d.id = @id", null, ("@id", id));
        return Read(cmd).FirstOrDefault();
    }, "Cannot read disk");

    public DiskDto? FindByIdentity(string serial, long totalBytes) => db.Run(c =>
    {
        using var cmd = Cmd(c, Select + " WHERE d.serial = @s AND d.total_bytes = @t", null, ("@s", serial), ("@t", totalBytes));
        return Read(cmd).FirstOrDefault();
    }, "Cannot read disk");

    public long Add(DiskDto dto) => db.Run(c =>
    {
        using var cmd = Cmd(c, """
            INSERT INTO dbo.disks(serial, total_bytes, label, fs, last_root, first_seen, last_scan)
            OUTPUT INSERTED.id VALUES(@s, @t, @l, @f, @r, @n, @ls)
            """, null, ("@s", dto.Serial), ("@t", dto.TotalBytes), ("@l", dto.Label), ("@f", dto.FileSystem),
            ("@r", dto.LastRoot), ("@n", dto.FirstSeen), ("@ls", dto.LastScan));
        return (long)cmd.ExecuteScalar()!;
    }, "Cannot add disk");

    public void UpdateSeen(long id, string label, string fileSystem, string root) => db.Run(c =>
        Exec(c, "UPDATE dbo.disks SET label=@l, fs=@f, last_root=@r WHERE id=@id",
            ("@l", label), ("@f", fileSystem), ("@r", root), ("@id", id)), "Cannot update disk");

    public void MarkScanCompleted(long id, string stamp) => db.Run(c =>
        Exec(c, "UPDATE dbo.disks SET last_scan=@n WHERE id=@id", ("@n", stamp), ("@id", id)), "Cannot update disk");

    static List<DiskDto> Read(SqlCommand cmd)
    {
        var list = new List<DiskDto>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new DiskDto
            {
                Id = r.GetInt64(0), Serial = r.GetString(1), TotalBytes = r.GetInt64(2),
                Label = r.IsDBNull(3) ? null : r.GetString(3), FileSystem = r.IsDBNull(4) ? null : r.GetString(4),
                LastRoot = r.IsDBNull(5) ? null : r.GetString(5), FirstSeen = r.GetString(6),
                LastScan = r.IsDBNull(7) ? null : r.GetString(7), FileCount = r.GetInt64(8),
            });
        return list;
    }
}
