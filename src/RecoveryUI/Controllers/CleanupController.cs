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
/// Clean up: Find (read-only) -> the list -> Move (after one more question) -> Undo if wanted.
/// Emptying the clean-up folder is separate and needs both deletion steps.
/// </summary>
public sealed class CleanupController(ICleanupView view, IMainView main, OperationRunner runner, CleanupService cleanup) : IPageController
{
    public Page Page => Page.Cleanup;
    public string Title => "Clean up";
    public IPageView View => view;

    const int MaxRows = 5000;
    CleanupPlan? plan;

    public void Attach()
    {
        view.FindRequested += (_, _) => Find();
        view.MoveRequested += (_, _) => Move();
        view.StopRequested += (_, _) => runner.Cancel();
        view.UndoRequested += (_, _) => Undo();
        view.EmptyTargetRequested += (_, _) => EmptyTarget();
        view.FilterChanged += (_, _) => ShowPlan();
        view.OpenInExplorerRequested += (_, path) =>
        {
            if (File.Exists(path)) ExplorerLauncher.Select(main, path);
            else main.ShowInfo("Not found", path + "\n\nis not there (any more).");
        };
    }

    public void OnShown() => RefreshUndo();

    void RefreshUndo() => Task.Run(() =>
    {
        var last = cleanup.LastRun();
        view.SetUndo(last is { } l ? $"Move the {l.Files:N0} files of the last clean up back ({Path.GetFileName(l.File)})." : null);
    });

    IProgress<ProgressInfo> Progress() => new Progress<ProgressInfo>(p =>
    {
        var vm = ProgressText.From(p);
        view.ShowProgress(vm);
        main.SetProgress(vm);
    });

    async void Find()
    {
        var r = view.Request;
        if (r.Source.Length == 0 || r.Target.Length == 0)
        {
            main.ShowInfo("Clean up", "Choose the folder to clean and where the unwanted files go.");
            return;
        }
        if (runner.IsBusy) { main.ShowInfo("Busy", "Another operation is running. Wait until it is done."); return; }
        plan = null;
        view.ShowRows([], "Looking…", false);
        view.ShowDecisions([]);
        view.SetRunning(true);
        CleanupPlan? found = null;
        string? error = null;
        // Find always judges every category; the ticks only choose what is shown and moved, so changing them needs no new Find.
        await runner.RunAsync("Finding", ct => found = cleanup.Find(new CleanupOptions(r.Source, r.Target, r.MasterRoot,
            CleanupCategories.All.ToHashSet()), Progress(), ct), ex => error = Message(ex));
        view.SetRunning(false);
        if (error != null) { view.ShowStatus(error, warning: true); return; }
        if (found == null) { view.ShowStatus("Stopped. Nothing was changed."); return; }
        plan = found;
        view.ShowDecisions(found.Decisions.Select(d => $"•  {d.Question}  {d.SourcePath}  /  master: {d.MasterPath}").ToList());
        ShowPlan();
    }

    /// <summary>The items of the plan that the person wants now (ticked categories; merge items always).</summary>
    IReadOnlyList<CleanupItem> Chosen()
    {
        if (plan == null) return [];
        var on = view.Request.Categories;
        return plan.Items.Where(i => i.Action == CleanupAction.ToMaster || (i.Category is { } c && on.Contains(c))).ToList();
    }

    void ShowPlan()
    {
        if (plan == null) return;
        var chosen = Chosen();
        var counts = CleanupCategories.All.Select(c =>
        {
            var items = plan.Items.Where(i => i.Category == c).ToList();
            return new CleanupCountViewModel(c, $"{c.FolderName()} ({items.Count:N0}{(items.Count > 0 ? ", " + Format.Bytes(items.Sum(i => i.Size)) : "")})", items.Count);
        }).ToList();
        view.ShowCounts(counts);

        var rows = chosen.OrderBy(i => i.Action).ThenBy(i => i.Category).ThenByDescending(i => i.Size).Take(MaxRows).Select(i => new CleanupRowViewModel
        {
            Item = i,
            File = Path.GetFileName(i.SourcePath),
            GoesTo = i.Action == CleanupAction.ToMaster ? Path.GetDirectoryName(i.DestPath)! : Path.GetRelativePath(plan.Target, Path.GetDirectoryName(i.DestPath)!),
            Why = i.Action == CleanupAction.ToMaster ? i.Reason : $"{i.Category!.Value.FolderName()}: {i.Reason}",
            Stays = i.Keep ?? "",
            Size = Format.Bytes(i.Size),
            From = Path.GetDirectoryName(i.SourcePath)!,
        }).ToList();

        var toTarget = chosen.Where(i => i.Action == CleanupAction.ToTarget).ToList();
        var toMaster = chosen.Where(i => i.Action == CleanupAction.ToMaster).ToList();
        var summary = chosen.Count == 0
            ? $"Checked {plan.FilesChecked:N0} files: nothing to move with these choices."
            : $"Checked {plan.FilesChecked:N0} files. {toTarget.Count:N0} unwanted ({Format.Bytes(toTarget.Sum(i => i.Size))}) would go to {plan.Target}"
              + (toMaster.Count > 0 ? $"; {toMaster.Count:N0} ({Format.Bytes(toMaster.Sum(i => i.Size))}) into the master folders" : "")
              + (chosen.Count > MaxRows ? $". The list shows the first {MaxRows:N0}." : ".")
              + (plan.Unreadable > 0 ? $" {plan.Unreadable:N0} files could not be read and stay." : "");
        view.ShowRows(rows, summary, chosen.Count > 0);
    }

    async void Move()
    {
        if (plan == null) return;
        var chosen = Chosen();
        if (chosen.Count == 0) return;
        if (runner.IsBusy) { main.ShowInfo("Busy", "Another operation is running. Wait until it is done."); return; }
        var lines = chosen.Where(i => i.Action == CleanupAction.ToTarget).GroupBy(i => i.Category!.Value)
            .Select(g => $"  •  {g.Key.FolderName()}: {g.Count():N0} files, {Format.Bytes(g.Sum(i => i.Size))}").ToList();
        var toMaster = chosen.Where(i => i.Action == CleanupAction.ToMaster).ToList();
        if (toMaster.Count > 0) lines.Add($"  •  into the master folders in {plan.MasterRoot}: {toMaster.Count:N0} files, {Format.Bytes(toMaster.Sum(i => i.Size))}");
        if (!main.Confirm("Move files",
                $"From {plan.Source}:\n\n{string.Join("\n", lines)}\n\nUnwanted files go to {plan.Target}\\<why>\\<type>. " +
                "The version you want stays where it is. Nothing is deleted or overwritten, and \"Undo last clean up\" puts it all back.\n\nMove them now?"))
            return;

        CleanupRunResult? result = null;
        string? error = null;
        view.SetRunning(true);
        await runner.RunAsync("Cleaning up", ct => result = cleanup.Execute(plan, chosen, Progress(), ct), ex => error = Message(ex));
        view.SetRunning(false);
        plan = null;
        view.ShowRows([], "", false);
        view.ShowDecisions([]);
        RefreshUndo();
        if (error != null) { main.ShowError("Clean up", error); return; }
        if (result == null) return;
        var text = $"Moved {result.Moved:N0} files ({Format.Bytes(result.MovedBytes)}).";
        if (result.Skipped > 0) text += $"\n{result.Skipped:N0} were not moved because they changed after Find (see the log).";
        if (result.Errors > 0) text += $"\n{result.Errors:N0} could not be moved (see the log).";
        if (!result.Completed) text += "\nStopped before the end; the rest is untouched.";
        text += "\n\n\"Undo last clean up\" puts them back. Press Find again to check the folder.";
        view.ShowStatus(text.Split('\n')[0]);
        main.ShowInfo("Clean up", text);
    }

    async void Undo()
    {
        if (runner.IsBusy) { main.ShowInfo("Busy", "Another operation is running. Wait until it is done."); return; }
        var last = cleanup.LastRun();
        if (last == null) { RefreshUndo(); return; }
        if (!main.Confirm("Undo last clean up", $"Move the {last.Value.Files:N0} files of the last clean up back to where they were?\n\n" +
                                                 "A file whose old place is taken by another file stays where it is."))
            return;
        CleanupRunResult? result = null;
        string? error = null;
        view.SetRunning(true);
        await runner.RunAsync("Undoing", ct => result = cleanup.UndoLast(Progress(), ct), ex => error = Message(ex));
        view.SetRunning(false);
        plan = null;
        view.ShowRows([], "", false);
        RefreshUndo();
        if (error != null) { main.ShowError("Undo", error); return; }
        if (result != null)
            main.ShowInfo("Undo", $"Moved {result.Moved:N0} files back." + (result.Skipped > 0 ? $"\n{result.Skipped:N0} stayed (see the log)." : ""));
    }

    async void EmptyTarget()
    {
        var target = view.Request.Target;
        if (target.Length == 0) { main.ShowInfo("Empty the clean-up folder", "Fill in where the unwanted files go first."); return; }
        if (runner.IsBusy) { main.ShowInfo("Busy", "Another operation is running. Wait until it is done."); return; }
        DeletionBatch batch;
        try { batch = await Task.Run(() => cleanup.PrepareEmpty(target)); }
        catch (RecoveryServiceException ex) { main.ShowError("Empty the clean-up folder", ex.Message); return; }
        if (batch.IsEmpty) { main.ShowInfo("Empty the clean-up folder", target + " is already empty."); return; }
        if (!DeletionDialogs.ApproveList(batch, $"Delete EVERYTHING in {target} for good? These are the files Clean up moved there " +
                                                "(and anything else you put there). This cannot be undone.")) return;
        CleanupRunResult? result = null;
        string? error = null;
        view.SetRunning(true);
        await runner.RunAsync("Deleting", ct => result = cleanup.Empty(target, batch, batch.Code, Progress(), ct), ex => error = Message(ex));
        view.SetRunning(false);
        if (error != null) { main.ShowError("Empty the clean-up folder", error); return; }
        if (result is { Moved: > 0 })
            main.ShowInfo("Empty the clean-up folder", $"Deleted {result.Moved:N0} files ({Format.Bytes(result.MovedBytes)}). The folder itself stays.");
        else main.ShowInfo("Empty the clean-up folder", "Nothing was deleted.");
    }

    static string Message(Exception ex)
    {
        if (ex is RecoveryServiceException) return ex.Message;
        Debug.WriteLine(ex);
        return "Unexpected error: " + ex.Message;
    }
}
