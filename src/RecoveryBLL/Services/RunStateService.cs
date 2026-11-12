using RecoveryBLL.Exceptions;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;

namespace RecoveryBLL.Services;

/// <summary>
/// Remembers what a run holds (WSL mounts, destinations being written) in a journal on disk, so that after a
/// crash, power loss or a killed process the next start can give it back:
///   - a Linux partition left attached by wsl --mount is unmounted (otherwise Windows cannot use the disk);
///   - half-written "*.rtpart" copies are found and listed. Deleting them, like every deletion, needs two steps:
///     approve the list by its code, then a final confirmation (<see cref="DeleteLeftovers"/>);
///   - a destination is never written by two runs at the same time.
/// Every frontend calls <see cref="ReleaseStale"/> at start-up; "rtcatalog cleanup" does it on request.
/// </summary>
public sealed class RunStateService(
    IRunStateStore store,
    IProcessInfo processes,
    IWslGateway wsl,
    IElevationInfo elevation,
    IFileStore files,
    IDeletionConfirmer confirmer,
    IOperationLog log)
{
    public const string TempExtension = ".rtpart";

    public string Location => store.Location;

    public IReadOnlyList<RunLease> Current() => Guard(() => store.Update(_ => { }));

    /// <summary>Records that this process holds <paramref name="target"/>. Refuses if another running process holds it.</summary>
    public void Hold(LeaseKind kind, string target)
    {
        RunLease? other = null;
        Guard(() => store.Update(list =>
        {
            other = list.FirstOrDefault(l => l.Kind == kind && SameTarget(l.Target, target) && !IsMine(l)
                                             && processes.IsAlive(l.ProcessId, l.ProcessStartTicks));
            if (other != null) return;
            list.RemoveAll(l => l.Kind == kind && SameTarget(l.Target, target) && IsMine(l));
            list.Add(new RunLease(kind, target, processes.CurrentId, processes.CurrentStartTicks, RepositoryGuard.Now()));
        }));
        if (other != null)
            throw new RecoveryServiceException(
                $"{Describe(kind, target)} is in use by another Recovery Toolkit run (process {other.ProcessId}, since {Format.Stamp(other.Since)} UTC). " +
                "Wait for it to finish or close it first.");
    }

    public void Release(LeaseKind kind, string target)
    {
        try { Guard(() => store.Update(list => list.RemoveAll(l => l.Kind == kind && SameTarget(l.Target, target) && IsMine(l)))); }
        catch (RecoveryServiceException ex) { log.Warn(ex.Message); }   // never let bookkeeping hide the real result
    }

    /// <summary>
    /// Frees what runs that are no longer alive held, without deleting anything: Linux disks are unmounted,
    /// destinations without leftovers are released, and leftover *.rtpart files are only listed (<see cref="CleanupResult.Leftovers"/>).
    /// </summary>
    public CleanupResult ReleaseStale()
    {
        var released = new List<string>();
        var problems = new List<string>();
        var all = Guard(() => store.Update(_ => { }));
        var stale = all.Where(IsStale).ToList();
        var drop = new List<RunLease>();
        foreach (var lease in stale)
        {
            switch (lease.Kind)
            {
                case LeaseKind.WslMount:
                    if (!int.TryParse(lease.Target, out var disk)) { drop.Add(lease); break; }
                    try
                    {
                        wsl.Unmount(disk);
                        released.Add($"Unmounted Linux disk {disk} (left attached by a run that stopped unexpectedly)");
                        drop.Add(lease);
                    }
                    catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        if (elevation.IsElevated)
                        {
                            released.Add($"Linux disk {disk} was no longer attached");   // as admin, failure means: not mounted
                            drop.Add(lease);
                        }
                        else
                            problems.Add($"Linux disk {disk} may still be attached to WSL. Start the toolkit as administrator, or run  wsl --unmount \\\\.\\PHYSICALDRIVE{disk}");
                    }
                    break;
                case LeaseKind.CopyDestination:
                    // Released once no half-written copies are left there; until then it stays listed as a reminder.
                    if (!Directory.Exists(lease.Target) || FindTemps(lease.Target, problems).Count == 0)
                    {
                        released.Add($"Released destination {lease.Target}");
                        drop.Add(lease);
                    }
                    break;
            }
        }
        if (drop.Count > 0) Guard(() => store.Update(list => list.RemoveAll(drop.Contains)));

        var leftovers = FindLeftovers(null, problems);
        foreach (var r in released) log.Info(r);
        foreach (var p in problems) log.Warn(p);
        if (!leftovers.IsEmpty)
            log.Warn($"{leftovers.Count:N0} half-written copies ({Format.Bytes(leftovers.Bytes)}) from an interrupted copy were found. " +
                     $"They are harmless; remove them with  rtcatalog cleanup --approve-delete {leftovers.Code}  (asks once more).");
        return new CleanupResult(released, 0, problems, Current().Where(l => !IsMine(l)).ToList()) { Leftovers = leftovers };
    }

    /// <summary>Step 1 input: the leftover *.rtpart files in destinations of dead runs, plus <paramref name="extraFolders"/>.</summary>
    public DeletionBatch FindLeftovers(IReadOnlyCollection<string>? extraFolders, List<string>? problems = null)
    {
        problems ??= [];
        var all = Guard(() => store.Update(_ => { }));
        var live = all.Where(l => l.Kind == LeaseKind.CopyDestination && !IsStale(l) && !IsMine(l)).ToList();
        var folders = all.Where(l => l.Kind == LeaseKind.CopyDestination && IsStale(l)).Select(l => l.Target).ToList();
        foreach (var folder in extraFolders ?? [])
        {
            var holder = live.FirstOrDefault(l => Overlaps(l.Target, folder));
            if (holder != null) problems.Add($"{folder}: a running copy (process {holder.ProcessId}) is writing there; not touched.");
            else folders.Add(Path.GetFullPath(folder));
        }
        var found = new Dictionary<string, DeletionCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders.Where(Directory.Exists))
            foreach (var temp in FindTemps(folder, problems))
                if (!live.Any(l => Overlaps(l.Target, temp)))
                    found.TryAdd(temp, new DeletionCandidate(temp, SafeLength(temp), "half-written copy left by an interrupted copy"));
        return new DeletionBatch(found.Values);
    }

    /// <summary>
    /// Deletes leftover *.rtpart files, only after both steps: (1) <paramref name="approveCode"/> is the code of the
    /// list as it is NOW, (2) the frontend's final confirmation. Each file is re-checked right before deleting.
    /// </summary>
    public CleanupResult DeleteLeftovers(IReadOnlyCollection<string>? extraFolders, string? approveCode)
    {
        var problems = new List<string>();
        var batch = FindLeftovers(extraFolders, problems);
        if (batch.IsEmpty) return new CleanupResult([], 0, problems, []) { Leftovers = batch };
        if (!batch.IsApprovedBy(approveCode))
        {
            problems.Add($"Not deleted: the approval code does not match the current list (code {batch.Code}). Check the list again, then approve that code.");
            return new CleanupResult([], 0, problems, []) { Leftovers = batch };
        }
        if (!confirmer.ConfirmFinal(batch))
        {
            log.Info("Removing half-written copies was not confirmed: nothing was deleted.");
            return new CleanupResult([], 0, problems, []) { Leftovers = batch };
        }

        long deleted = 0;
        foreach (var f in batch.Files)
        {
            try
            {
                // Re-check: still a half-written copy, unchanged, and no live run started writing there meanwhile.
                if (!f.Path.EndsWith(TempExtension, StringComparison.OrdinalIgnoreCase) || !files.Exists(f.Path) || files.Length(f.Path) != f.Size)
                { problems.Add($"Not removing {f.Path}: gone or changed since the list was made."); continue; }
                if (Current().Any(l => l.Kind == LeaseKind.CopyDestination && !IsStale(l) && !IsMine(l) && Overlaps(l.Target, f.Path)))
                { problems.Add($"Not removing {f.Path}: a running copy writes there now."); continue; }
                files.Delete(f.Path);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { problems.Add($"Cannot delete {f.Path}: {ex.Message}"); }
        }
        if (deleted > 0) log.Info($"Deleted {deleted:N0} half-written copies (*{TempExtension})");
        foreach (var p in problems) log.Warn(p);
        ReleaseStale();   // destinations without leftovers are released now
        return new CleanupResult([], deleted, problems, Current().Where(l => !IsMine(l)).ToList()) { Leftovers = FindLeftovers(extraFolders) };
    }

    IReadOnlyList<string> FindTemps(string folder, List<string> problems)
    {
        try { return files.FindTemps(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { problems.Add($"Cannot search {folder}: {ex.Message}"); return []; }
    }

    long SafeLength(string path)
    {
        try { return files.Length(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    bool IsMine(RunLease l) => l.ProcessId == processes.CurrentId && l.ProcessStartTicks == processes.CurrentStartTicks;
    bool IsStale(RunLease l) => !IsMine(l) && !processes.IsAlive(l.ProcessId, l.ProcessStartTicks);

    static bool SameTarget(string a, string b) => string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    static bool Overlaps(string a, string b)
    {
        var x = a.TrimEnd('\\') + "\\";
        var y = b.TrimEnd('\\') + "\\";
        return x.StartsWith(y, StringComparison.OrdinalIgnoreCase) || y.StartsWith(x, StringComparison.OrdinalIgnoreCase);
    }

    static string Describe(LeaseKind kind, string target) =>
        kind == LeaseKind.WslMount ? $"Linux disk {target}" : $"Destination {target}";

    static T Guard<T>(Func<T> work)
    {
        try { return work(); }
        catch (IOException ex) { throw new RecoveryServiceException("Cannot read or write the run-state journal: " + ex.Message, ex); }
        catch (UnauthorizedAccessException ex) { throw new RecoveryServiceException("Cannot read or write the run-state journal: " + ex.Message, ex); }
    }
}
