using System.Security.Principal;
using RecoveryBLL.Interfaces;

namespace RecoveryDAL.Storage;

public sealed class FileSystemWalker : IFileSystemWalker
{
    static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,   // throw, so unreadable folders are reported and not mistaken for empty ones
        AttributesToSkip = 0,         // include hidden/system files: recovered data is often hidden
        ReturnSpecialDirectories = false,
    };

    public IEnumerable<WalkedFile> Walk(string folder, IReadOnlySet<string> skipFolderNames, IReadOnlyCollection<string> skipFolderPaths,
                                        Action<string, Exception> onFolderError, CancellationToken ct)
    {
        var root = Path.GetPathRoot(folder) ?? folder;
        var stack = new Stack<string>();
        stack.Push(folder);
        while (stack.Count > 0 && !ct.IsCancellationRequested)
        {
            var dir = stack.Pop();
            bool inRoot = dir.TrimEnd('\\').Length <= root.TrimEnd('\\').Length;
            var found = new List<WalkedFile>();
            try
            {
                foreach (var info in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", Options))
                {
                    if (info is DirectoryInfo sub)
                    {
                        if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) continue;   // junctions/symlinks: avoid loops
                        if (skipFolderNames.Contains(sub.Name)) continue;
                        if (skipFolderPaths.Any(p => string.Equals(p, sub.FullName, StringComparison.OrdinalIgnoreCase))) continue;
                        stack.Push(sub.FullName);
                    }
                    else if (info is FileInfo fi)
                    {
                        found.Add(new WalkedFile(fi.FullName, fi.Name, fi.Length, fi.LastWriteTimeUtc.Ticks, inRoot));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                onFolderError(dir, ex);
            }
            foreach (var f in found) yield return f;   // yield outside the try: files read before an error still count
        }
    }
}

/// <summary>Reads source files. Every read has a deadline (<see cref="TimeoutStream"/>) so a failing disk cannot hang a run.</summary>
public sealed class FileContentReader(TimeSpan? readTimeout = null) : IFileContentReader
{
    public static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(60);

    public Stream OpenRead(string fullPath, bool sequential) =>
        new TimeoutStream(
            new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1,
                           (sequential ? FileOptions.SequentialScan : FileOptions.RandomAccess) | FileOptions.Asynchronous),
            readTimeout ?? DefaultReadTimeout, fullPath);
}

public sealed class WindowsElevationInfo : RecoveryBLL.Interfaces.IElevationInfo
{
    public bool IsElevated
    {
        get
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
