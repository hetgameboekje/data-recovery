namespace RecoveryCatalog;

/// <summary>Step 1: walk a folder or drive and record path, size and modified time. Reads no file content.</summary>
static class Scanner
{
    static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "$RECYCLE.BIN", "RECYCLER", "System Volume Information", "Config.Msi",
        "$WinREAgent", "$SysReset", "$Windows.~BT", "$Windows.~WS",
        "_RecoveryToolkit", // the toolkit's logs, PhotoRec sessions and half-sorted raw output
    };

    static readonly HashSet<string> SkipRootFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "pagefile.sys", "hiberfil.sys", "swapfile.sys", "DumpStack.log", "DumpStack.log.tmp",
    };

    public sealed record Result(long Files, long Bytes, long DirErrors, int MarkedMissing, bool Completed);

    public static Result Run(Catalog cat, string scanPath, IReadOnlyCollection<string> excludes, CancellationToken ct)
    {
        var full = Path.GetFullPath(scanPath);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException("Folder not found: " + full);
        var root = Path.GetPathRoot(full) ?? throw new ArgumentException("No root in " + full);
        var vol = Volumes.Identify(root) ?? throw new IOException("Cannot read volume information of " + root);
        var diskId = cat.EnsureDisk(vol);
        var prefix = full.Length > root.Length ? full[root.Length..].TrimEnd('\\') : "";
        var stamp = Catalog.Now();

        Console.WriteLine($"Scanning {full}  ->  {cat.DiskName(diskId)}  ({vol.FileSystem}, serial {vol.Serial})");

        var excludeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excludePaths = new List<string>();
        foreach (var e in excludes)
        {
            if (e.Contains('\\') || e.Contains(':')) excludePaths.Add(Path.GetFullPath(e).TrimEnd('\\'));
            else excludeNames.Add(e);
        }

        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,      // throw, so unreadable folders are counted and not mistaken for empty ones
            AttributesToSkip = 0,            // include hidden and system files: recovered data is often hidden
            ReturnSpecialDirectories = false,
        };

        var batch = new List<Catalog.ScannedFile>(5000);
        long files = 0, bytes = 0, dirErrors = 0;
        var progress = new Progress("Scan");
        var stack = new Stack<string>();
        stack.Push(full);

        while (stack.Count > 0 && !ct.IsCancellationRequested)
        {
            var dir = stack.Pop();
            try
            {
                foreach (var info in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", opts))
                {
                    if (info is DirectoryInfo sub)
                    {
                        if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) continue; // junctions/symlinks: avoid loops
                        if (SkipDirs.Contains(sub.Name) || excludeNames.Contains(sub.Name)) continue;
                        if (excludePaths.Count > 0 && excludePaths.Any(p => string.Equals(p, sub.FullName, StringComparison.OrdinalIgnoreCase))) continue;
                        stack.Push(sub.FullName);
                        continue;
                    }

                    var fi = (FileInfo)info;
                    if (fi.Name.EndsWith(".rtpart", StringComparison.OrdinalIgnoreCase)) continue; // toolkit's half-written copies
                    if (dir.Length == root.Length && SkipRootFiles.Contains(fi.Name)) continue;

                    var rel = fi.FullName[root.Length..];
                    var ext = Categories.Ext(fi.Name);
                    batch.Add(new(rel, fi.Length, fi.LastWriteTimeUtc.Ticks, ext, Categories.Of(ext)));
                    files++;
                    bytes += fi.Length;
                    progress.Set(files, bytes);

                    if (batch.Count >= 5000)
                    {
                        cat.Upsert(diskId, batch, stamp);
                        batch.Clear();
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                dirErrors++;
                Log.Warn($"Cannot read folder {dir}: {ex.Message}");
            }
        }

        if (batch.Count > 0) cat.Upsert(diskId, batch, stamp);
        progress.Done(files, bytes);

        bool completed = !ct.IsCancellationRequested;
        int missing = 0;
        // Only flag missing files after a full walk; an interrupted scan simply continues next time.
        // A folder that could not be read is not proof its files are gone, so skip flagging then too.
        if (completed && dirErrors == 0) missing = cat.MarkMissing(diskId, prefix, stamp);
        return new Result(files, bytes, dirErrors, missing, completed);
    }
}
