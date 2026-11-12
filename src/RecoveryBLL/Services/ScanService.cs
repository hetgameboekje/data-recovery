using System.Diagnostics;
using RecoveryBLL.Dtos;
using RecoveryBLL.Exceptions;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;
using RecoveryBLL.Validation;

namespace RecoveryBLL.Services;

/// <summary>Step 1 of cataloguing: record path, size and modified time of every file. Reads no file content.</summary>
public sealed class ScanService(
    DiskService disks,
    IFileRepository files,
    IFileSystemWalker walker,
    IVolumeProvider volumes,
    DictionaryService dictionaries,
    IOperationLog log)
{
    const int BatchSize = 5000;

    public ScanResult Scan(ScanRequest request, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        ScanRequestValidator.Validate(request);
        var full = Path.GetFullPath(request.Path);
        if (!Directory.Exists(full)) throw new RecoveryServiceException("Folder not found: " + full);

        var identity = request.IdentityOverride
            ?? volumes.Identify(Path.GetPathRoot(full) ?? throw new RecoveryServiceException("No drive in " + full))
            ?? throw new RecoveryServiceException("Cannot read the volume information of " + full);
        var root = identity.Root.EndsWith('\\') ? identity.Root : identity.Root + "\\";
        if (!(full + "\\").StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new RecoveryServiceException($"{full} is not on {root}");

        var diskId = disks.EnsureDisk(identity);
        if (request.IdentityOverride != null) disks.RegisterExtraRoot(diskId, root);
        var prefix = full.Length > root.Length ? full[root.Length..].TrimEnd('\\') : "";
        var stamp = RepositoryGuard.Now();
        var dict = dictionaries.Current;

        log.Info($"Scanning {full}  ->  {disks.Get(diskId)?.DisplayName}  ({identity.FileSystem})");

        var skipNames = new HashSet<string>(dict.SkipFolders, StringComparer.OrdinalIgnoreCase);
        var skipPaths = new List<string>();
        foreach (var e in request.Excludes.Where(e => !string.IsNullOrWhiteSpace(e)))
        {
            if (e.Contains('\\') || e.Contains(':')) skipPaths.Add(Path.GetFullPath(e).TrimEnd('\\'));
            else skipNames.Add(e.Trim());
        }

        long count = 0, bytes = 0, folderErrors = 0;
        var sw = Stopwatch.StartNew();
        long lastReport = 0;
        var batch = new List<ScannedFileDto>(BatchSize);

        foreach (var f in walker.Walk(full, skipNames, skipPaths, (folder, ex) =>
                 {
                     folderErrors++;
                     log.Warn($"Cannot read folder {folder}: {ex.Message}");
                 }, ct))
        {
            var ext = Path.GetExtension(f.Name) is { Length: > 1 } e ? e[1..].ToLowerInvariant() : "";
            if (dict.PartialFileExtensions.Contains(ext)) continue;
            if (f.InRoot && dict.SkipRootFiles.Contains(f.Name)) continue;

            batch.Add(new ScannedFileDto(f.FullPath[root.Length..], f.Size, f.MtimeTicks, ext, dict.CategoryOf(ext)));
            count++;
            bytes += f.Size;
            if (batch.Count >= BatchSize) Flush();
            if (sw.ElapsedMilliseconds - lastReport >= 250)
            {
                lastReport = sw.ElapsedMilliseconds;
                progress?.Report(new ProgressInfo("Scan", count, 0, bytes, 0, sw.Elapsed));
            }
        }
        Flush();
        progress?.Report(new ProgressInfo("Scan", count, count, bytes, bytes, sw.Elapsed));

        bool completed = !ct.IsCancellationRequested;
        int missing = 0;
        // Only after a full walk without unreadable folders is "not seen" proof that a file is gone.
        if (completed && folderErrors == 0)
        {
            missing = RepositoryGuard.Run(() => files.MarkMissingNotSeen(diskId, prefix, stamp), "Cannot update the catalog");
            disks.MarkScanCompleted(diskId, stamp);
        }

        var result = new ScanResult(full, diskId, count, bytes, folderErrors, missing, completed);
        log.Info($"Scanned {full}: {count:N0} files, {Format.Bytes(bytes)}"
                 + (folderErrors > 0 ? $", {folderErrors:N0} unreadable folder(s)" : "")
                 + (missing > 0 ? $", {missing:N0} catalogued file(s) no longer present" : "")
                 + (completed ? "" : " - INTERRUPTED, run again to continue"));
        return result;

        void Flush()
        {
            if (batch.Count == 0) return;
            RepositoryGuard.Run(() => files.UpsertScanned(diskId, batch, stamp), "Cannot save scanned files");
            batch.Clear();
        }
    }
}
