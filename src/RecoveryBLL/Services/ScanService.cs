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
    IProtectedLocations protectedLocations,
    ICatalogStore catalog,
    AppSettings settings,
    IKeepAwake keepAwake,
    DictionaryService dictionaries,
    IOperationLog log)
{
    const int BatchSize = 5000;

    public ScanResult Scan(ScanRequest request, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        ScanRequestValidator.Validate(request);
        // Real drives/folders are checked against the Windows/AppData list; WSL mounts carry their own identity.
        var full = request.IdentityOverride == null
            ? PathSafetyValidator.Validate(request.Path, request.IsDestination ? PathUse.Destination : PathUse.ScanSource, protectedLocations)
            : Path.GetFullPath(request.Path);
        if (!Directory.Exists(full)) throw new RecoveryServiceException("Folder not found: " + full);

        var identity = request.IdentityOverride
            ?? volumes.Identify(Path.GetPathRoot(full) ?? throw new RecoveryServiceException("No drive in " + full))
            ?? throw new RecoveryServiceException("Cannot read the volume information of " + full);
        var root = identity.Root.EndsWith('\\') ? identity.Root : identity.Root + "\\";
        if (!(full + "\\").StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new RecoveryServiceException($"{full} is not on {root}");

        if (request.IdentityOverride == null && !request.IsDestination) CheckNothingIsWrittenTo(identity, root);
        using var awake = keepAwake.Begin("Recovery Toolkit is cataloguing files");

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
            if (batch.Count > 0 && !Directory.Exists(root))
                log.Warn($"{root} disappeared during the scan (unplugged?). What was listed is kept; nothing is marked as deleted. Scan again when it is back.");
            if (batch.Count == 0) return;
            RepositoryGuard.Run(() => files.UpsertScanned(diskId, batch, stamp), "Cannot save scanned files");
            batch.Clear();
        }
    }

    /// <summary>
    /// The toolkit writes its catalog, logs and run-state journal while it works. On a disk you recover from,
    /// every write can overwrite deleted files that could still be undeleted, so that is refused.
    /// The Windows drive is written to all the time anyway: there it is only a warning.
    /// </summary>
    void CheckNothingIsWrittenTo(VolumeIdentity source, string sourceRoot)
    {
        var written = new List<(string What, string Path)>();
        if (catalog.LocalFile is { } db) written.Add(("catalog", db));
        written.Add(("log folder", settings.LogDirectory));
        var onWindowsDrive = new List<string>();
        foreach (var (what, path) in written)
        {
            string? root;
            try { root = Path.GetPathRoot(Path.GetFullPath(path)); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { continue; }
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal)) continue;
            var v = volumes.Identify(root);
            if (v == null || v.Serial != source.Serial || v.TotalBytes != source.TotalBytes) continue;

            if (string.Equals(root.TrimEnd('\\'), protectedLocations.SystemDrive.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                onWindowsDrive.Add(what);
                continue;
            }
            throw new RecoveryServiceException(
                $"The {what} ({path}) is on {sourceRoot}, the disk you want to scan. Writing there can overwrite deleted files that " +
                "could still be recovered. Put the catalog and logs on another disk (DB_CONNECTION and LOG_DIR in .env, or --db) and start again.");
        }
        if (onWindowsDrive.Count > 0)
            log.Warn($"The {string.Join(" and ", onWindowsDrive)} {(onWindowsDrive.Count == 1 ? "is" : "are")} on the Windows drive you are scanning. Fine for normal files, but do not use " +
                     "this run to recover DELETED files from this drive: new writes can overwrite them.");
    }
}
