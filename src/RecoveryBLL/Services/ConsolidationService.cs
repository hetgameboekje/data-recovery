using System.Diagnostics;
using System.IO.Hashing;
using RecoveryBLL.Dtos;
using RecoveryBLL.Exceptions;
using RecoveryBLL.Fingerprinting;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;
using RecoveryBLL.Validation;

namespace RecoveryBLL.Services;

/// <summary>
/// Phase 2: copy one clean set of everything onto the destination disks. The source disks are only read.
///   Plan    - catalog the destinations, fingerprint what is still open, and build the plan (dry run).
///   Execute - the same plan, then copy: write "&lt;name&gt;.rtpart", check the hash against the catalog, rename,
///             and record the new file in the catalog. Stop any time; running it again continues
///             (copied files come back as "already there").
/// </summary>
public sealed class ConsolidationService(
    DiskService disks,
    ScanService scanner,
    FingerprintService fingerprints,
    IConsolidationRepository repository,
    IVolumeProvider volumes,
    IFileContentReader reader,
    IFileStore store,
    IProtectedLocations protectedLocations,
    RunStateService runState,
    IDeletionConfirmer confirmer,
    IKeepAwake keepAwake,
    IOperationLog log)
{
    const int Buffer = 1024 * 1024;

    /// <summary>Catalogs the destinations (listing only), fingerprints what is open on plugged-in disks, and plans.</summary>
    public CopyPlan Plan(ConsolidateOptions options, IProgress<ProgressInfo>? progress, CancellationToken ct, bool fingerprintFirst = true)
    {
        if (options.Destinations.Count == 0)
            throw new RecoveryServiceException("Give at least one destination (main disk first, then overflow disks).");

        var targets = new List<(DestinationRequest Request, string Full, VolumeIdentity Volume)>();
        foreach (var d in options.Destinations)
        {
            var given = d.Path.Length == 2 && d.Path[1] == ':' ? d.Path + "\\" : d.Path;
            // Never the Windows drive as a whole, Windows/Program Files/ProgramData/AppData, or a folder containing them.
            var full = d.IdentityOverride == null
                ? PathSafetyValidator.Validate(given, PathUse.Destination, protectedLocations)
                : Path.GetFullPath(given);
            if (!Directory.Exists(full)) throw new RecoveryServiceException("Destination not found: " + full);
            var volume = d.IdentityOverride ?? volumes.Identify(Path.GetPathRoot(full) ?? full)
                ?? throw new RecoveryServiceException("Cannot read the volume information of " + full);
            targets.Add((d, full, volume));
        }

        // Catalog each destination folder (only that folder: a folder on C: must not mean scanning all of C:).
        // Its content counts as "already there"; so does anything catalogued earlier elsewhere on that disk.
        var destinations = new List<DestinationPlan>();
        var systemRoot = protectedLocations.SystemDrive.TrimEnd('\\') + "\\";
        foreach (var (request, full, volume) in targets)
        {
            ct.ThrowIfCancellationRequested();
            var root = volume.Root.EndsWith('\\') ? volume.Root : volume.Root + "\\";
            var scan = scanner.Scan(new ScanRequest(full, [], request.IdentityOverride, IsDestination: true), progress, ct);
            if (destinations.Any(x => x.DiskId == scan.DiskId))
                throw new RecoveryServiceException($"{full} is on the same disk as another destination.");
            var free = volumes.GetFreeBytes(Path.GetPathRoot(full) ?? root);
            bool isSystem = request.IdentityOverride == null && string.Equals(Path.GetPathRoot(full), systemRoot, StringComparison.OrdinalIgnoreCase);
            var reserve = isSystem ? Math.Max(options.ReserveBytes, ConsolidateOptions.SystemDriveMinFree) : options.ReserveBytes;
            var usable = Math.Max(0, free - reserve);
            if (request.MaxBytes is { } max) usable = Math.Min(usable, max);
            destinations.Add(new DestinationPlan
            {
                DiskId = scan.DiskId,
                Name = disks.Get(scan.DiskId)?.DisplayName ?? root,
                Root = root,
                Prefix = full.Length > root.Length ? full[root.Length..].TrimEnd('\\') : "",
                FileSystem = volume.FileSystem,
                IsSystemDrive = isSystem,
                ReserveBytes = reserve,
                FreeBytes = free,
                UsableBytes = usable,
            });
        }
        if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);

        if (fingerprintFirst) fingerprints.Run(new FingerprintOptions(AutoThreads: true, ReadImageInfo: false), progress, ct);
        ct.ThrowIfCancellationRequested();

        var catalogued = disks.GetCatalogued().ToDictionary(d => d.Id);
        var folders = catalogued.ToDictionary(kv => kv.Key, kv => DiskFolderName(kv.Value));
        var files = RepositoryGuard.Run(repository.GetPlanFiles, "Cannot read the catalog");
        var built = ConsolidationPlanner.Build(files, destinations, folders, options.CopyDuplicates);

        var mounted = disks.GetMountedRoots();
        var waiting = built.Items.Where(i => i.NeedsCopy && !mounted.ContainsKey(i.SourceDiskId))
            .GroupBy(i => i.SourceDiskId)
            .Where(g => catalogued.ContainsKey(g.Key))
            .Select(g => (catalogued[g.Key], (long)g.Count(), g.Sum(i => i.Size)))
            .ToList();

        var warnings = new List<string>();
        // Files catalogued elsewhere on a destination disk count as "already there" (they are on that disk). Say so,
        // otherwise a destination folder on the same disk as the sources looks like "nothing to copy" for no reason.
        long sameDiskFiles = 0, sameDiskBytes = 0;
        foreach (var d in destinations)
        {
            var outside = files.Where(f => f.DiskId == d.DiskId && !(d.Prefix.Length == 0
                                           || f.Path.StartsWith(d.Prefix + "\\", StringComparison.OrdinalIgnoreCase))).ToList();
            sameDiskFiles += outside.Count;
            sameDiskBytes += outside.Sum(f => f.Size);
            if (outside.Count > 0)
                warnings.Add($"{outside.Count:N0} catalogued files ({Format.Bytes(outside.Sum(f => f.Size))}) are on the same disk as {d.FullPath}. " +
                             "They count as already there and are not copied again. To copy them, choose a destination on another disk.");
        }
        foreach (var d in destinations.Where(d => d.IsSystemDrive))
            warnings.Add($"{d.FullPath} is on the Windows drive: at least {Format.Bytes(d.ReserveBytes)} is kept free there. A separate backup disk is safer.");
        if (built.PendingFiles > 0)
            warnings.Add($"{built.PendingFiles:N0} files ({Format.Bytes(built.PendingBytes)}) are not fingerprinted far enough to know whether they are duplicates; " +
                         "they are planned as unique. Plug in their disks and run 'hash' first for an exact plan.");
        if (destinations.Any(d => d.IsFat) && built.Items.Any(i => i.Size > ConsolidationPlanner.Fat32MaxFileSize))
            warnings.Add("FAT32 destinations cannot hold files over 4 GB; those go to another destination or are listed as 'no room'.");

        var rootOf = destinations.ToDictionary(d => d.DiskId, d => d.Root);
        var deletions = new DeletionBatch(built.Evictions.Select(e => new DeletionCandidate(
            Path.Combine(rootOf[e.DiskId], e.Path), e.Size,
            $"identical copy in _Duplicates; the same content stays at {Path.Combine(rootOf.GetValueOrDefault(e.KeptDiskId, ""), e.KeptPath)}")));
        var longPaths = built.Items.Count(i => i.NeedsCopy && Path.Combine(rootOf[i.DestDiskId!.Value], i.DestPath!).Length >= 260);
        if (longPaths > 0)
            warnings.Add($"{longPaths:N0} destination paths are longer than 260 characters. They are copied, but Explorer and older programs may not open them.");
        if (!deletions.IsEmpty)
            warnings.Add($"To make room, {deletions.Count:N0} old duplicates ({Format.Bytes(deletions.Bytes)}) would be deleted from _Duplicates. " +
                         $"Nothing is deleted unless you approve this list with --approve-delete {deletions.Code} and confirm once more when the copy starts.");

        return new CopyPlan
        {
            Items = built.Items,
            Evictions = built.Evictions,
            Deletions = deletions,
            LongPaths = longPaths,
            SameDiskFiles = sameDiskFiles,
            SameDiskBytes = sameDiskBytes,
            Destinations = destinations,
            PendingFiles = built.PendingFiles,
            PendingBytes = built.PendingBytes,
            Warnings = warnings,
            WaitingDisks = waiting,
        };
    }

    /// <summary>
    /// Plans, then copies. Never writes to a source disk. The only deletions are planned old duplicates on a
    /// destination, and only after both steps: the approval code of that exact list, and the final confirmation.
    /// </summary>
    public CopyResult Execute(ConsolidateOptions options, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var plan = Plan(options, progress, ct);
        if (plan.PendingFiles > 0 && !options.AllowPending)
            throw new RecoveryServiceException(
                $"{plan.PendingFiles:N0} files ({Format.Bytes(plan.PendingBytes)}) are not fingerprinted yet, so duplicates cannot be told apart. " +
                "Plug in their disks and run 'hash' (or allow it explicitly with --allow-pending).");

        // Journal the destinations: refuses a second run writing there, and lets a later start clean up after a crash.
        var held = new List<string>();
        try
        {
            foreach (var d in plan.Destinations)
            {
                runState.Hold(LeaseKind.CopyDestination, d.FullPath);
                held.Add(d.FullPath);
            }
            using var awake = keepAwake.Begin("Recovery Toolkit is copying files");
            return Copy(plan, options, progress, ct);
        }
        finally
        {
            foreach (var h in held) runState.Release(LeaseKind.CopyDestination, h);
        }
    }

    CopyResult Copy(CopyPlan plan, ConsolidateOptions options, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var mounted = new Dictionary<long, string>(disks.GetMountedRoots());
        var destById = plan.Destinations.ToDictionary(d => d.DiskId);
        // What this run may write per destination: the planned usable space, plus what removed duplicates free up.
        var budget = plan.Destinations.ToDictionary(d => d.DiskId, d => d.UsableBytes);
        var written = plan.Destinations.ToDictionary(d => d.DiskId, _ => 0L);
        var (evicted, evictedBytes, notApproved) = DeleteOldDuplicates(plan, options, destById, budget, ct);

        // Unique content first, then duplicates; per source disk in path order (sequential reads).
        var todo = plan.Items.Where(i => i.NeedsCopy)
            .OrderBy(i => i.Action == CopyAction.Duplicate ? 1 : 0)
            .ThenBy(i => i.SourceDiskId)
            .ThenBy(i => i.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        long total = todo.Count, totalBytes = todo.Sum(i => i.Size);
        long copied = 0, copiedBytes = 0, waitingForDisk = 0, noRoom = 0, errors = 0, done = 0, doneBytes = 0;
        var full = new HashSet<long>();          // destinations that are full or gone for this run
        var skipped = new HashSet<long>();       // source disks that are gone or failing for this run
        var timeouts = new Dictionary<long, int>();
        var failing = new List<string>();
        var sw = Stopwatch.StartNew();
        long lastReport = 0;
        log.Info($"Copying {total:N0} files, {Format.Bytes(totalBytes)}");

        foreach (var item in todo)
        {
            if (ct.IsCancellationRequested) break;
            var dest = destById[item.DestDiskId!.Value];
            if (full.Contains(dest.DiskId)) { noRoom++; Advance(item.Size); continue; }
            if (skipped.Contains(item.SourceDiskId) || !mounted.ContainsKey(item.SourceDiskId)) { waitingForDisk++; Advance(item.Size); continue; }
            var need = ConsolidationPlanner.Alloc(item.Size);
            if (written[dest.DiskId] + need > budget[dest.DiskId]
                || volumes.GetFreeBytes(Path.GetPathRoot(dest.Root) ?? dest.Root) - dest.ReserveBytes < item.Size)
            {
                noRoom++;
                Advance(item.Size);
                continue;
            }

            for (int attempt = 0; ; attempt++)
            {
                var source = Path.Combine(mounted[item.SourceDiskId], item.SourcePath);
                try
                {
                    CopyOne(item, source, dest, Path.Combine(dest.Root, item.DestPath!), options.Verify, b => { doneBytes += b; Report(false); }, ct);
                    copied++;
                    copiedBytes += item.Size;
                    written[dest.DiskId] += need;
                    timeouts.Remove(item.SourceDiskId);
                    break;
                }
                catch (OperationCanceledException) { goto stopped; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (IsDiskFull(ex)) { full.Add(dest.DiskId); noRoom++; log.Warn($"{dest.Name} is full; files planned there wait for the next run."); break; }

                    bool sourceGone = !Directory.Exists(mounted[item.SourceDiskId]);
                    bool destGone = !Directory.Exists(dest.Root);
                    if ((sourceGone || destGone) && attempt == 0 && options.WaitForUnplugged)
                    {
                        var ids = new List<long>();
                        if (sourceGone) ids.Add(item.SourceDiskId);
                        if (destGone) ids.Add(dest.DiskId);
                        if (WaitForDisks(ids, mounted, destById, options.UnplugWait, progress, ct)) continue;   // back: retry this file
                    }
                    if (sourceGone)
                    {
                        skipped.Add(item.SourceDiskId);
                        waitingForDisk++;
                        log.Warn($"{DiskName(item.SourceDiskId)} is unplugged; its files wait for the next run.");
                    }
                    else if (destGone)
                    {
                        full.Add(dest.DiskId);
                        waitingForDisk++;
                        log.Warn($"{dest.Name} is unplugged; files planned there wait for the next run.");
                    }
                    else if (ex is ReadTimeoutException)
                    {
                        errors++;
                        var n = timeouts[item.SourceDiskId] = timeouts.GetValueOrDefault(item.SourceDiskId) + 1;
                        log.Warn($"Copy {source}: {ex.Message}");
                        if (n >= FailingDiskTimeouts)
                        {
                            skipped.Add(item.SourceDiskId);
                            failing.Add(DiskName(item.SourceDiskId));
                            log.Warn($"{DiskName(item.SourceDiskId)}: {n} reads in a row did not finish. The disk may be failing: stop using it, " +
                                     "make a sector image first (old toolkit: Disk tools > Imaging), then catalog the image. Its other files are skipped.");
                        }
                    }
                    else { errors++; log.Warn($"Copy {source}: {ex.Message}"); }
                    break;
                }
            }
            done++;
            Report(false);
        }
        stopped:
        Report(true);

        bool completed = !ct.IsCancellationRequested;
        log.Info($"Copied {copied:N0} files ({Format.Bytes(copiedBytes)})"
                 + (waitingForDisk > 0 ? $", {waitingForDisk:N0} wait for a disk that is not plugged in" : "")
                 + (noRoom > 0 ? $", {noRoom:N0} did not fit" : "")
                 + (errors > 0 ? $", {errors:N0} errors (see the log)" : "")
                 + (completed ? "" : " - STOPPED, run again to continue"));
        // Every copy was flushed to the disk before it was renamed, so nothing is waiting in a cache.
        log.Info($"Safe to unplug: {string.Join(", ", plan.Destinations.Select(d => d.Name))} (every copy is flushed to disk).");
        return new CopyResult(copied, copiedBytes, plan.Files(CopyAction.AlreadyThere), waitingForDisk, noRoom, errors, evicted, evictedBytes, completed)
        {
            DeletionsNotApproved = notApproved,
            FailingDisks = failing,
        };

        void Advance(long size) { done++; doneBytes += size; Report(false); }
        void Report(bool force)
        {
            var ms = sw.ElapsedMilliseconds;
            if (!force && ms - lastReport < 250) return;
            lastReport = ms;
            progress?.Report(new ProgressInfo("Copy", done, total, doneBytes, totalBytes, sw.Elapsed));
        }
    }

    const int FailingDiskTimeouts = 3;

    /// <summary>
    /// Deletes the planned old duplicates, but only after BOTH steps: (1) the code of this exact list was given,
    /// (2) the frontend's final confirmation. Then each file is re-checked: its kept copy must still exist with
    /// the same size. Without approval nothing is deleted and the files that needed the room simply wait.
    /// </summary>
    (long Evicted, long EvictedBytes, long NotApproved) DeleteOldDuplicates(CopyPlan plan, ConsolidateOptions options,
        Dictionary<long, DestinationPlan> destById, Dictionary<long, long> budget, CancellationToken ct)
    {
        if (plan.Deletions.IsEmpty) return (0, 0, 0);
        if (!plan.Deletions.IsApprovedBy(options.ApproveDeleteCode))
        {
            log.Warn($"{plan.Deletions.Count:N0} old duplicates ({Format.Bytes(plan.Deletions.Bytes)}) would make room, but deleting needs approval. " +
                     $"Check the list with 'plan', then add --approve-delete {plan.Deletions.Code}. Until then the files that need that room wait.");
            return (0, 0, plan.Deletions.Count);
        }
        if (!confirmer.ConfirmFinal(plan.Deletions))
        {
            log.Info("Deleting old duplicates was not confirmed: nothing was deleted.");
            return (0, 0, plan.Deletions.Count);
        }

        long evicted = 0, evictedBytes = 0;
        foreach (var e in plan.Evictions)
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.Combine(destById[e.DiskId].Root, e.Path);
            var kept = destById.TryGetValue(e.KeptDiskId, out var keptDest) ? Path.Combine(keptDest.Root, e.KeptPath) : null;
            try
            {
                // Re-check right before deleting: still the planned file, and its kept copy really there with the same size.
                if (kept == null || !store.Exists(kept) || store.Length(kept) != e.Size)
                {
                    log.Warn($"Not removing {path}: its kept copy {kept ?? e.KeptPath} is missing or changed. Run plan again.");
                    continue;
                }
                if (!store.Exists(path) || store.Length(path) != e.Size)
                {
                    log.Warn($"Not removing {path}: it is gone or changed since the plan.");
                    continue;
                }
                store.Delete(path);
                RepositoryGuard.Run(() => repository.MarkMissing(e.FileId), "Cannot update the catalog");
                evicted++;
                evictedBytes += e.Size;
                budget[e.DiskId] += ConsolidationPlanner.Alloc(e.Size);
                log.Info($"Removed old duplicate to make room: {path}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Warn($"Cannot remove {path}: {ex.Message}");
            }
        }
        return (evicted, evictedBytes, 0);
    }

    /// <summary>
    /// A disk in use was unplugged: wait until it is back (any USB port, any drive letter; recognised by its
    /// identity, not its letter), or until the time is up or the run is stopped.
    /// </summary>
    bool WaitForDisks(IReadOnlyList<long> ids, Dictionary<long, string> mounted, Dictionary<long, DestinationPlan> destById,
                      TimeSpan maxWait, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var names = string.Join(" and ", ids.Select(DiskName));
        log.Warn($"{names} was unplugged. Plug it back in (any port; the drive letter may change). " +
                 $"Waiting up to {maxWait.TotalMinutes:0} minutes; stop the run to quit. The file being copied is copied again.");
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < maxWait)
        {
            progress?.Report(new ProgressInfo("Waiting for " + names, 0, 0, 0, 0, sw.Elapsed));
            if (ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(2))) return false;
            var now = disks.GetMountedRoots();
            if (!ids.All(now.ContainsKey) || !ids.All(id => Directory.Exists(now[id]))) continue;
            foreach (var id in ids)
            {
                mounted[id] = now[id];
                if (destById.TryGetValue(id, out var d)) d.Root = now[id].EndsWith('\\') ? now[id] : now[id] + "\\";
            }
            log.Info($"{names} is back ({string.Join(", ", ids.Select(id => now[id]))}); continuing.");
            return true;
        }
        log.Warn($"{names} did not come back within {maxWait.TotalMinutes:0} minutes.");
        return false;
    }

    string DiskName(long diskId) => disks.Get(diskId)?.DisplayName ?? $"Disk {diskId}";

    void CopyOne(PlanItem item, string source, DestinationPlan dest, string target, bool verify, Action<long> onBytes, CancellationToken ct)
    {
        var temp = target + ".rtpart";
        byte[] quick, hash;
        try
        {
            using (var s = reader.OpenRead(source, sequential: true))
            {
                quick = ContentHasher.Quick(item.FileId, s, item.Size, false).QuickHash!;
                s.Seek(0, SeekOrigin.Begin);
                var x = new XxHash128();
                using var d = store.CreateTemp(temp);
                var buf = new byte[Buffer];
                long total = 0;
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    x.Append(buf.AsSpan(0, n));
                    d.Write(buf, 0, n);
                    total += n;
                    onBytes(n);
                }
                if (total != item.Size) throw new FileChangedException("file changed while copying; scan the disk again");
                if (d is FileStream fs) fs.Flush(flushToDisk: true);
                hash = x.GetCurrentHash();
            }
            if (item.FullHash != null && !item.FullHash.AsSpan().SequenceEqual(hash))
                throw new FileChangedException("content differs from the catalog (changed since the scan, or a read error); scan the disk again");
            if (verify)
            {
                using var check = reader.OpenRead(temp, sequential: true);
                if (!ContentHasher.Full(item.FileId, check, item.Size, false).FullHash!.AsSpan().SequenceEqual(hash))
                    throw new IOException("verification failed: the copy on the destination reads back differently");
            }

            // A file can already be at the target when a previous run stopped between renaming and recording it.
            var relative = item.DestPath!;
            if (store.Exists(target))
            {
                using (var existing = reader.OpenRead(target, sequential: true))
                    if (existing.Length == item.Size && ContentHasher.Full(item.FileId, existing, item.Size, false).FullHash!.AsSpan().SequenceEqual(hash))
                    {
                        store.Delete(temp);
                        Record();
                        return;
                    }
                (target, relative) = FreeName(dest, relative);
            }
            store.Commit(temp, target, item.MtimeTicks);
            Record();

            void Record() => RepositoryGuard.Run(() => repository.RecordCopy(
                new CopyRecordDto(item.FileId, dest.DiskId, relative, store.LastWriteTicks(Path.Combine(dest.Root, relative)), quick, hash, RepositoryGuard.Now())),
                "Cannot record the copy in the catalog");
        }
        catch
        {
            try { if (store.Exists(temp)) store.Delete(temp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    (string Full, string Relative) FreeName(DestinationPlan dest, string relative)
    {
        var dir = Path.GetDirectoryName(relative) ?? "";
        var name = Path.GetFileNameWithoutExtension(relative);
        var ext = Path.GetExtension(relative);
        for (int n = 2; ; n++)
        {
            var rel = Path.Combine(dir, $"{name} ({n}){ext}");
            var full = Path.Combine(dest.Root, rel);
            if (!store.Exists(full)) return (full, rel);
        }
    }

    static bool IsDiskFull(Exception ex) => (ex.HResult & 0xFFFF) is 112 or 39;   // ERROR_DISK_FULL, ERROR_HANDLE_DISK_FULL

    /// <summary>Folder name for a source disk inside _Duplicates / _OlderVersions, e.g. "Folders_1A2B3C4D".</summary>
    internal static string DiskFolderName(Disk d) =>
        ConsolidationPlanner.SafeName((d.Label.Length == 0 ? "Disk" : d.Label) + "_" + d.Serial);
}
