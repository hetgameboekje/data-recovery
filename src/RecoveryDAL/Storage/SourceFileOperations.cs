using RecoveryBLL.Interfaces;

namespace RecoveryDAL.Storage;

/// <summary>Clean up: moves and removes files where they are. Only used for files the person saw in a list and confirmed.</summary>
public sealed class SourceFileOperations : ISourceFileOperations
{
    public void Rename(string fromPath, string toPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(toPath)!);
        File.Move(fromPath, toPath, overwrite: false);   // never overwrites; a folder created here gets no special attributes
    }

    public void Delete(string fullPath)
    {
        var info = new FileInfo(fullPath);
        if (!info.Exists) return;
        if (info.IsReadOnly) info.IsReadOnly = false;
        info.Delete();
    }

    public bool DeleteFolderIfEmpty(string folder)
    {
        var info = new DirectoryInfo(folder);
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.EnumerateFileSystemInfos().Any()) return false;
        info.Delete(recursive: false);
        return true;
    }
}
