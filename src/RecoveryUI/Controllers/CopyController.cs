using System.Diagnostics;
using RecoveryBLL;
using RecoveryBLL.Exceptions;
using RecoveryBLL.Models;
using RecoveryBLL.Services;
using RecoveryUI.Infrastructure;
using RecoveryUI.ViewModels;
using RecoveryUI.Views;

namespace RecoveryUI.Controllers;

/// <summary>
/// The Copy page: destinations -> plan (dry run) -> copy. Deleting old duplicates needs both steps:
/// step 1 here (review the exact list, which approves its code), step 2 in the service right before deleting
/// (<see cref="DeletionDialogs.ConfirmFinal"/>). Any change to destinations or options throws the plan and the approval away.
/// </summary>
public sealed class CopyController(
    ICopyView view, IMainView main, OperationRunner runner, ConsolidationService copies, DiskService disks) : IPageController
{
    public Page Page => Page.Copy;
    public string Title => "Copy";
    public IPageView View => view;

    CopyPlan? plan;
    string? approvedCode;

    public void Attach()
    {
        view.RefreshDrivesRequested += (_, _) => LoadDrives();
        view.SettingsChanged += (_, _) => Invalidate();
        view.PlanRequested += (_, _) => CheckPlan();
        view.ReviewDeletionsRequested += (_, _) => ReviewDeletions();
        view.ExportRequested += (_, _) => Export();
        view.StartRequested += (_, _) => Start();
        view.StopRequested += (_, _) => runner.Cancel();
    }

    public void OnShown() => LoadDrives();

    void LoadDrives() => Task.Run(() =>
    {
        try { view.ShowDrives(disks.GetPluggedInDrives().Select(DriveRowViewModel.From).ToList()); }
        catch (RecoveryServiceException ex) { main.ShowError("Drives", ex.Message); }
    });

    void Invalidate()
    {
        plan = null;
        approvedCode = null;
        view.ShowPlan(null);
    }

    ConsolidateOptions Options()
    {
        var o = view.Options;
        return new ConsolidateOptions(
            view.Destinations.Select(d => new DestinationRequest(d.Path, d.MaxBytes)).ToList(),
            o.CopyDuplicates, o.ReserveBytes, o.AllowPending, o.Verify, approvedCode, o.WaitForUnplugged);
    }

    async void CheckPlan()
    {
        if (view.Destinations.Count == 0) { main.ShowInfo("No backup disk", "First choose your backup disk in step 1."); return; }
        if (runner.IsBusy) { main.ShowInfo("Busy", "Another operation is running. Stop it first or wait until it finishes."); return; }
        Invalidate();
        view.SetRunning(true);
        CopyPlan? result = null;
        string? error = null;
        await runner.RunAsync("Checking the plan", ct => result = copies.Plan(Options(), Progress(), ct), ex => error = Message(ex));
        view.SetRunning(false);
        if (result == null)
        {
            view.ShowResult(error ?? "Stopped.", false);
            return;
        }
        plan = result;
        view.ShowPlan(ToViewModel(result, view.Options));
    }

    void ReviewDeletions()
    {
        if (plan == null || plan.Deletions.IsEmpty) return;
        // Step 1 of 2: the person sees exactly these files; approving stores the code of this list.
        if (DeletionDialogs.ApproveList(plan.Deletions,
                "To make room for unique files, these identical copies in _Duplicates would be deleted. The same content stays on the backup disk."))
        {
            approvedCode = plan.Deletions.Code;
            view.SetDeletionApproval($"Step 1 of 2 done: {plan.Deletions.Count:N0} old duplicates approved. You confirm once more when the copy starts.", true);
        }
        else
        {
            approvedCode = null;
            view.SetDeletionApproval(DeletionText(plan), false);
        }
    }

    void Export()
    {
        if (plan == null) return;
        var path = view.PickCsvFile();
        if (path == null) return;
        try
        {
            PlanExport.WriteCsv(plan, disks.GetCatalogued().ToDictionary(d => d.Id, d => d.DisplayName), path);
            main.ShowInfo("Copy plan", $"Saved {plan.Items.Count:N0} rows to {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or RecoveryServiceException)
        {
            main.ShowError("Copy plan", ex.Message);
        }
    }

    async void Start()
    {
        if (plan == null) { main.ShowInfo("Check the plan first", "Press \"Check the plan\" in step 2 first."); return; }
        if (runner.IsBusy) { main.ShowInfo("Busy", "Another operation is running. Stop it first or wait until it finishes."); return; }
        var targets = string.Join("\n", plan.Destinations.Select((d, i) => $"  {(i == 0 ? "Main" : "Overflow")}: {d.FullPath}  ({d.Name})"));
        var deletionNote = plan.Deletions.IsEmpty ? ""
            : approvedCode != null ? $"\n\n{plan.Deletions.Count:N0} approved old duplicates may be deleted; you are asked once more before that."
            : $"\n\n{plan.Deletions.Count:N0} old duplicates are NOT approved for deletion, so files that need that room wait.";
        if (!view.Confirm("Start copying",
                $"Copy {plan.FilesToCopy:N0} files ({Format.Bytes(plan.BytesToCopy)}) to:\n{targets}\n\nYour source disks are only read.{deletionNote}"))
            return;

        view.SetRunning(true);
        CopyResult? result = null;
        string? error = null;
        await runner.RunAsync("Copying", ct => result = copies.Execute(Options(), Progress(), ct), ex => error = Message(ex));
        view.SetRunning(false);
        Invalidate();   // the catalog changed: a new check shows what is left
        if (result == null) { view.ShowResult(error ?? "Stopped.", false); return; }
        view.ShowResult(ResultText(result), result.Completed && result.Errors == 0);
    }

    static string ResultText(CopyResult r)
    {
        var lines = new List<string> { $"Copied {r.Copied:N0} files ({Format.Bytes(r.CopiedBytes)})." };
        if (r.Evicted > 0) lines.Add($"Removed {r.Evicted:N0} old duplicates ({Format.Bytes(r.EvictedBytes)}) to make room.");
        if (r.DeletionsNotApproved > 0) lines.Add($"{r.DeletionsNotApproved:N0} old duplicates were not deleted (not approved or not confirmed); files that need that room wait.");
        if (r.WaitingForDisk > 0) lines.Add($"{r.WaitingForDisk:N0} files wait for a disk that is not plugged in.");
        if (r.NoRoom > 0) lines.Add($"{r.NoRoom:N0} files did not fit.");
        if (r.Errors > 0) lines.Add($"{r.Errors:N0} files could not be copied (see the log in Expert mode).");
        foreach (var f in r.FailingDisks) lines.Add($"{f} kept timing out and was skipped: it may be failing. Image it before trying again.");
        lines.Add(r.Completed ? "Check the plan again to see whether anything is left. The backup disks are safe to unplug."
                              : "Stopped. Start again to continue where it stopped.");
        return string.Join(Environment.NewLine, lines);
    }

    static CopyPlanViewModel ToViewModel(CopyPlan p, CopyOptionsViewModel o)
    {
        var notes = new List<string>(p.Warnings);
        foreach (var (disk, files, bytes) in p.WaitingDisks)
            notes.Add($"Plug in {disk.DisplayName} to copy its {files:N0} files ({Format.Bytes(bytes)}).");
        bool pendingBlocks = p.PendingFiles > 0 && !o.AllowPending;
        if (pendingBlocks)
            notes.Add("Copying is blocked until those files are fingerprinted (Report page: \"Fingerprint the rest\"), or allow it in the options (Expert mode).");

        string verdict;
        if (!p.UniqueFits)
            verdict = $"Does not fit: {Format.Bytes(p.UniqueNoRoomBytes)} of unique data has no room ({Format.Bytes(p.UsableBytes)} usable). Add an overflow disk.";
        else if (p.FilesToCopy == 0)
            verdict = "Nothing left to copy: everything is already on the backup disk.";
        else
            verdict = $"Everything fits: {Format.Bytes(p.UniqueBytes)} of unique data, {Format.Bytes(p.UsableBytes)} usable."
                      + (p.DuplicatesNoRoom > 0 ? $" {p.DuplicatesNoRoom:N0} duplicates don't fit; that's fine, one copy of their content is kept." : "");

        return new CopyPlanViewModel
        {
            ToCopy = Format.Bytes(p.BytesToCopy),
            ToCopyCaption = $"{p.FilesToCopy:N0} files",
            Unique = Format.Bytes(p.UniqueBytes),
            Duplicates = Format.Bytes(p.DuplicateBytes),
            DuplicatesCaption = o.CopyDuplicates ? "only into leftover space" : "not copied (switched off)",
            Already = (p.Files(CopyAction.AlreadyThere) + p.SameDiskFiles).ToString("N0"),
            AlreadyCaption = $"files, {Format.Bytes(p.Bytes(CopyAction.AlreadyThere) + p.SameDiskBytes)}",
            Verdict = verdict,
            Fits = p.UniqueFits,
            Destinations = p.Destinations.Select(d => new DestinationRowViewModel
            {
                Name = d.Name, Folder = d.FullPath, Free = Format.Bytes(d.FreeBytes), Planned = Format.Bytes(d.PlannedBytes),
                Files = d.PlannedFiles.ToString("N0"), Usable = Format.Bytes(d.UsableBytes), Reserve = Format.Bytes(d.ReserveBytes),
                FileSystem = d.FileSystem, Evicted = d.EvictedBytes > 0 ? Format.Bytes(d.EvictedBytes) : "-",
            }).ToList(),
            Notes = notes,
            HasDeletions = !p.Deletions.IsEmpty,
            Deletions = DeletionText(p),
            CanStart = p.FilesToCopy > 0 && !pendingBlocks,
        };
    }

    static string DeletionText(CopyPlan p) => p.Deletions.IsEmpty ? "" :
        $"To make room, {p.Deletions.Count:N0} old duplicates ({Format.Bytes(p.Deletions.Bytes)}) in _Duplicates would be deleted. " +
        "Nothing is deleted unless you review and approve the list (step 1) and confirm again when the copy starts (step 2). " +
        "Without that, the files that need the room simply wait.";

    IProgress<ProgressInfo> Progress() => new Progress<ProgressInfo>(p =>
    {
        var vm = ProgressText.From(p);
        view.ShowProgress(vm);
        main.SetProgress(vm);
    });

    static string Message(Exception ex)
    {
        if (ex is RecoveryServiceException or OperationCanceledException) return ex is OperationCanceledException ? "Stopped." : ex.Message;
        Debug.WriteLine(ex);
        return "Unexpected error: " + ex.Message;
    }
}
