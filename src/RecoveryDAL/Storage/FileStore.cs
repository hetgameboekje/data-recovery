using RecoveryBLL.Interfaces;

namespace RecoveryDAL.Storage;

/// <summary>Writes to the destination disks. Nothing here ever touches a source disk.</summary>
public sealed class FileStore : IFileStore
{
    public Stream CreateTemp(string fullPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        return new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1, FileOptions.SequentialScan);
    }

    public void Commit(string tempPath, string finalPath, long mtimeTicksUtc)
    {
        File.SetLastWriteTimeUtc(tempPath, new DateTime(mtimeTicksUtc, DateTimeKind.Utc));
        File.Move(tempPath, finalPath, overwrite: false);
    }

    public bool Exists(string fullPath) => File.Exists(fullPath);

    public long LastWriteTicks(string fullPath) => File.GetLastWriteTimeUtc(fullPath).Ticks;

    public long Length(string fullPath) => new FileInfo(fullPath).Length;

    static readonly EnumerationOptions TempSearch = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,   // never follow junctions/symlinks out of the destination
        MatchType = MatchType.Simple,
    };

    public IReadOnlyList<string> FindTemps(string folder) => Directory.EnumerateFiles(folder, "*.rtpart", TempSearch).ToList();

    public void Delete(string fullPath)
    {
        var info = new FileInfo(fullPath);
        if (!info.Exists) return;
        if (info.IsReadOnly) info.IsReadOnly = false;
        info.Delete();
    }
}
