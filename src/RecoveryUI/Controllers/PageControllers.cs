using System.Diagnostics;
using RecoveryBLL;
using RecoveryBLL.Exceptions;
using RecoveryBLL.Models;
using RecoveryBLL.Services;
using RecoveryUI.Infrastructure;
using RecoveryUI.ViewModels;
using RecoveryUI.Views;

namespace RecoveryUI.Controllers;

/// <summary>A controller owns one page: it loads data from the BLL into its view and handles the view's events.</summary>
public interface IPageController
{
    Page Page { get; }
    string Title { get; }
    IPageView View { get; }
    void OnShown();
}

/// <summary>Turns BLL progress into what the views show (percent, speed, time left).</summary>
static class ProgressText
{
    public static ProgressViewModel From(ProgressInfo p)
    {
        if (p.Total <= 0)
            return new(p.Stage, $"{p.Done:N0} files, {Format.Bytes(p.Bytes)}", 0, true);
        var fraction = p.TotalBytes > 0 ? (double)p.Bytes / p.TotalBytes : p.Fraction;
        var eta = fraction > 0.02 && p.Done < p.Total ? TimeSpan.FromSeconds(p.Elapsed.TotalSeconds / fraction - p.Elapsed.TotalSeconds) : (TimeSpan?)null;
        var speed = p.TotalBytes > 0 ? $", {Format.Bytes((long)p.BytesPerSecond)}/s" : "";
        var left = eta is { } e ? $", about {(e.TotalHours >= 1 ? $"{(int)e.TotalHours} h {e.Minutes} min" : e.TotalMinutes >= 1 ? $"{(int)e.TotalMinutes} min" : "<1 min")} left" : "";
        return new(p.Stage, $"{p.Done:N0} of {p.Total:N0} files{speed}{left}", (int)(fraction * 100), false);
    }
}

public sealed class HomeController(IHomeView view, ReportService reports) : IPageController
{
    public Page Page => Page.Home;
    public string Title => "Home";
    public IPageView View => view;
    public event EventHandler<Page>? NavigateRequested;

    public void Attach() => view.StepClicked += (_, p) => NavigateRequested?.Invoke(this, p);

    public void OnShown() => Task.Run(() =>
    {
        try
        {
            var r = reports.GetReport();
            var next = r.Disks.Count == 0
                ? "Next step: plug in a disk and catalog it (step 1)."
                : r.HasPendingOnPluggedInDisks
                    ? $"Next step: {Format.Bytes(r.PendingBytes)} still needs fingerprinting - open the report and press \"Fingerprint the rest\"."
                    : r.PendingUnpluggedDisks.Count > 0
                        ? $"Next step: plug in {string.Join(", ", r.PendingUnpluggedDisks.Select(d => d.DisplayName))} to finish fingerprinting."
                        : "All catalogued disks are fingerprinted. Catalog more disks, or open the report.";
            view.ShowSummary(new HomeSummaryViewModel
            {
                Disks = r.Disks.Count.ToString("N0"),
                Files = r.TotalFiles.ToString("N0"),
                Unique = Format.Bytes(r.UniqueBytes),
                Extra = Format.Bytes(r.RedundantBytes),
                Pending = Format.Bytes(r.PendingBytes),
                NextStep = next,
            });
        }
        catch (RecoveryServiceException ex)
        {
            view.ShowSummary(new HomeSummaryViewModel { Disks = "-", Files = "-", Unique = "-", Extra = "-", Pending = "-", NextStep = ex.Message });
        }
    });
}

public sealed class DisksController(IDisksView view, IMainView main, DiskService disks, LinuxDiskService linux, DictionaryService dictionaries) : IPageController
{
    public Page Page => Page.Disks;
    public string Title => "Disks";
    public IPageView View => view;
    public event EventHandler<LinuxScanRequest>? LinuxScanRequested;

    public void Attach()
    {
        view.RefreshRequested += (_, _) => OnShown();
        view.LinuxScanRequested += (_, r) => LinuxScanRequested?.Invoke(this, r);
    }

    public void OnShown()
    {
        Task.Run(() =>
        {
            try
            {
                var plugged = disks.GetPluggedInDrives();
                view.ShowDrives(plugged.Select(DriveRowViewModel.From).ToList());
                var mounted = disks.GetMountedRoots();
                view.ShowCatalogued(disks.GetCatalogued().Select(d => new CatalogDiskRowViewModel
                {
                    Name = d.DisplayName,
                    NowOn = mounted.TryGetValue(d.Id, out var root) ? root : "unplugged",
                    Files = d.FileCount.ToString("N0"),
                    Size = Format.Bytes(d.TotalBytes),
                    LastScan = Format.Stamp(d.LastScan),
                    FileSystem = d.FileSystem,
                    FirstSeen = Format.Stamp(d.FirstSeen),
                }).ToList());
                var bus = dictionaries.Current.BusTypes;
                view.ShowDevices(disks.GetPhysicalDevices().Select(d => new DeviceRowViewModel
                {
                    Number = d.Number.ToString(),
                    Model = d.Model,
                    Size = Format.Bytes(d.SizeBytes),
                    Connection = bus.TryGetValue(d.Bus, out var b) ? b.Name : d.Bus.ToString(),
                    Kind = d.Kind,
                    Trim = d.SupportsTrim switch { true => "yes", false => "no", _ => "?" },
                    Removable = d.Removable ? "yes" : "no",
                }).ToList());
            }
            catch (RecoveryServiceException ex) { main.ShowError("Disks", ex.Message); }
        });
        Task.Run(() =>
        {
            if (!linux.IsWslInstalled) { view.ShowLinuxStatus("WSL 2 is not installed (install with: wsl --install).", false); return; }
            var distros = string.Join(", ", linux.Distributions);
            view.ShowLinuxStatus(linux.IsElevated
                ? $"Ready. Distributions: {distros}. The partition is mounted read-only, catalogued, then unmounted."
                : $"WSL found ({distros}), but mounting needs administrator rights: restart the toolkit as administrator.",
                linux.IsElevated);
        });
    }
}

public sealed class ScanController(
    IScanView view, IMainView main, OperationRunner runner,
    ScanService scans, FingerprintService fingerprints, DiskService disks, LinuxDiskService linux) : IPageController
{
    public Page Page => Page.Scan;
    public string Title => "Catalog disks";
    public IPageView View => view;

    public void Attach()
    {
        view.StartRequested += (_, _) => Start();
        view.StopRequested += (_, _) => runner.Cancel();
        view.RefreshRequested += (_, _) => OnShown();
    }

    public void OnShown() => Task.Run(() =>
    {
        try { view.ShowDrives(disks.GetPluggedInDrives().Select(DriveRowViewModel.From).ToList()); }
        catch (RecoveryServiceException ex) { main.ShowError("Drives", ex.Message); }
    });

    void Start()
    {
        var targets = view.SelectedTargets;
        if (targets.Count == 0)
        {
            main.ShowInfo("Nothing selected", "Tick at least one drive or folder in step 1.");
            return;
        }
        var o = view.Options;
        Run("Cataloguing", ct =>
        {
            long files = 0, bytes = 0;
            foreach (var t in targets)
            {
                if (ct.IsCancellationRequested) break;
                var r = scans.Scan(new ScanRequest(t, o.Excludes), Progress(), ct);
                files += r.Files;
                bytes += r.Bytes;
            }
            if (!o.ListOnly && !ct.IsCancellationRequested)
                fingerprints.Run(Options(o), Progress(), ct);
            return ct.IsCancellationRequested
                ? "Stopped. Start again to continue where it stopped; finished work is kept."
                : $"Done: {files:N0} files ({Format.Bytes(bytes)}) catalogued. Open the report to see what is unique and what is duplicated.";
        });
    }

    /// <summary>Called from the Disks page: mount a Linux partition read-only, catalog it, unmount.</summary>
    public void StartLinux(LinuxScanRequest req)
    {
        var o = view.Options;
        Run($"Cataloguing Linux disk {req.DiskNumber} partition {req.Partition}", ct =>
        {
            var mount = linux.Mount(req.DiskNumber, req.Partition, req.FileSystem);
            try
            {
                var r = scans.Scan(new ScanRequest(mount.Path, o.Excludes, mount.Identity), Progress(), ct);
                if (!o.ListOnly && !ct.IsCancellationRequested) fingerprints.Run(Options(o), Progress(), ct);
                return $"Done: {r.Files:N0} files ({Format.Bytes(r.Bytes)}) catalogued from the Linux partition.";
            }
            finally { linux.Unmount(mount); }
        });
    }

    static FingerprintOptions Options(ScanOptionsViewModel o) =>
        new(Math.Max(1, o.ReadersPerDisk), o.Sha256, o.ReadImageInfo, AutoThreads: o.ReadersPerDisk == 0);

    IProgress<ProgressInfo> Progress() => new Progress<ProgressInfo>(p =>
    {
        var vm = ProgressText.From(p);
        view.ShowProgress(vm);
        main.SetProgress(vm);
    });

    async void Run(string name, Func<CancellationToken, string> work)
    {
        if (runner.IsBusy) { main.ShowInfo("Busy", "Another operation is running. Stop it first or wait until it finishes."); return; }
        view.SetRunning(true);
        string result = "";
        var ok = await runner.RunAsync(name, ct => result = work(ct), ex =>
        {
            result = ex is RecoveryServiceException ? ex.Message : "Unexpected error: " + ex.Message;
            if (ex is not RecoveryServiceException) Debug.WriteLine(ex);
        });
        view.SetRunning(false);
        view.ShowResult(result, ok && !result.StartsWith("Stopped"));
    }
}

public sealed class ReportController(
    IReportView view, IMainView main, OperationRunner runner, ReportService reports, FingerprintService fingerprints,
    DuplicateCheckService duplicateCheck) : IPageController
{
    public Page Page => Page.Report;
    public string Title => "Report";
    public IPageView View => view;

    public void Attach()
    {
        view.RefreshRequested += (_, _) => OnShown();
        view.HashPendingRequested += (_, _) => HashPending();
        view.CheckDuplicatesRequested += (_, _) => CheckDuplicates();
        view.DuplicateGroupsRequested += (_, n) => Task.Run(() =>
        {
            try
            {
                view.ShowDuplicateGroups(reports.GetLargestDuplicateGroups(n).Select(g => new DuplicateGroupViewModel(
                    $"{g.Group.Copies} copies × {Format.Bytes(g.Group.Size)}  —  {Format.Bytes(g.Group.Size * (g.Group.Copies - 1))} extra",
                    g.Copies.Select(c => $"{c.Modified:yyyy-MM-dd HH:mm}   {c.FullPath}   [{c.Disk?.DisplayName}]").ToList())).ToList());
            }
            catch (RecoveryServiceException ex) { main.ShowError("Duplicates", ex.Message); }
        });
    }

    public void OnShown() => Task.Run(() =>
    {
        try
        {
            var r = reports.GetReport();
            view.ShowReport(new ReportViewModel
            {
                Total = Format.Bytes(r.TotalBytes),
                TotalCaption = $"{r.TotalFiles:N0} files on {r.Disks.Count} disk(s)",
                Unique = Format.Bytes(r.UniqueBytes),
                Extra = Format.Bytes(r.RedundantBytes),
                Pending = Format.Bytes(r.PendingBytes),
                Needed = r.PendingBytes > 0
                    ? $"One copy of everything needs {Format.Bytes(r.UniqueBytes)} to {Format.Bytes(r.NeededMaxBytes)} of space."
                    : $"One copy of everything needs {Format.Bytes(r.UniqueBytes)} of space.",
                PendingHint = r.PendingUnpluggedDisks.Count > 0
                    ? $"To decide the rest, plug in: {string.Join(", ", r.PendingUnpluggedDisks.Select(d => d.DisplayName))}."
                    : r.HasPendingOnPluggedInDisks ? "Some files still share a size with others and need fingerprinting." : "",
                CanHashPending = r.HasPendingOnPluggedInDisks,
                Types = r.Types.Select(t => new TypeRowViewModel
                {
                    Type = t.MediaType, Files = t.Files.ToString("N0"), Total = Format.Bytes(t.TotalBytes),
                    Unique = Format.Bytes(t.UniqueBytes), Extra = Format.Bytes(t.RedundantBytes), Pending = Format.Bytes(t.PendingBytes),
                }).ToList(),
                Disks = r.Disks.Select(d => new DiskSummaryRowViewModel
                {
                    Disk = d.Disk.DisplayName, NowOn = d.MountedRoot ?? "unplugged", Files = d.Files.ToString("N0"),
                    Total = Format.Bytes(d.TotalBytes), OnlyCopy = Format.Bytes(d.OnlyCopyBytes), Duplicated = Format.Bytes(d.DuplicatedBytes),
                    OnOtherDisk = Format.Bytes(d.DuplicatedOnOtherDiskBytes), Pending = Format.Bytes(d.PendingBytes),
                    Empty = d.EmptyFiles.ToString("N0"), Errors = d.ErrorFiles.ToString("N0"), LastScan = Format.Stamp(d.Disk.LastScan),
                }).ToList(),
            });
        }
        catch (RecoveryServiceException ex) { main.ShowError("Report", ex.Message); }
    });

    /// <summary>Independent check: re-read every copy of 20 random duplicate groups and compare them with SHA-256.</summary>
    async void CheckDuplicates()
    {
        if (runner.IsBusy) { main.ShowInfo("Busy", "Another operation is running (see the bottom of the window). Check the duplicates when it is done."); return; }
        var progress = new Progress<ProgressInfo>(p => main.SetProgress(ProgressText.From(p)));
        DuplicateCheckResult? r = null;
        await runner.RunAsync("Checking duplicates", ct => r = duplicateCheck.CheckSample(20, progress, ct),
            ex => main.ShowError("Check duplicates", ex.Message));
        if (r == null) return;

        // Expert tab: every checked group with its verdict and the SHA-256 of each copy.
        view.ShowDuplicateGroups(r.Groups.Select(g => new DuplicateGroupViewModel(
            $"{(g.Identical ? "IDENTICAL" : g.Different ? "DIFFERENT" : "not checked")}  —  {g.Copies.Count} copies × {Format.Bytes(g.Size)}",
            g.Copies.Select(c => $"{(c.Sha256 ?? "-")[..Math.Min(16, (c.Sha256 ?? "-").Length)]}   {c.FullPath}{(c.Problem != null ? "   (" + c.Problem + ")" : "")}").ToList())).ToList());

        var examples = string.Join("\n\n", r.Groups.Where(g => g.Identical || g.Different).Take(4).Select(g =>
            $"{(g.Identical ? "Identical" : "DIFFERENT")} ({Format.Bytes(g.Size)}):\n" + string.Join("\n", g.Copies.Take(3).Select(c => "   " + c.FullPath))));
        var verdict = r.Groups.Count == 0
            ? "There are no duplicate groups to check yet (fingerprinting decides what is a duplicate)."
            : r.Different == 0
                ? $"All {r.Identical} checked groups are really identical: every copy was re-read and matched byte for byte (SHA-256)."
                : $"{r.Different} of {r.Groups.Count} groups are NOT identical. A file changed after it was scanned, or a read failed: scan those disks again (details in the log).";
        var notChecked = r.NotChecked > 0 ? $"\n{r.NotChecked} groups could not be checked (a disk is unplugged or a copy could not be read)." : "";
        main.ShowInfo("Check duplicates", $"{verdict}{notChecked}\nRead {Format.Bytes(r.BytesRead)}.\n\n{examples}\n\nThe full list is in Expert mode, tab \"Largest duplicate groups\".");
    }

    async void HashPending()
    {
        if (runner.IsBusy) { main.ShowInfo("Busy", "Another operation is running."); return; }
        var progress = new Progress<ProgressInfo>(p => main.SetProgress(ProgressText.From(p)));
        await runner.RunAsync("Fingerprinting", ct => fingerprints.Run(new FingerprintOptions(AutoThreads: true), progress, ct),
            ex => main.ShowError("Fingerprinting", ex.Message));
        OnShown();
    }
}

public sealed class SettingsController(
    ISettingsView view, IMainView main, OperationRunner runner, AppSettings settings,
    DictionaryService dictionaries, MergeService merges, UiLog log) : IPageController
{
    public Page Page => Page.Settings;
    public string Title => "Settings";
    public IPageView View => view;

    public void Attach()
    {
        view.ReloadDictionariesRequested += (_, _) =>
        {
            try { dictionaries.Reload(); OnShown(); main.ShowInfo("Dictionaries", "Reloaded."); }
            catch (RecoveryServiceException ex) { main.ShowError("Dictionaries", ex.Message); }
        };
        view.OpenLogFolderRequested += (_, _) => OpenFolder(settings.LogDirectory);
        view.OpenConfigFolderRequested += (_, _) =>
            OpenFolder(settings.EnvFile != null ? Path.GetDirectoryName(settings.EnvFile)! : AppContext.BaseDirectory);
        view.MergeRequested += (_, _) => Merge();
    }

    public void OnShown()
    {
        string dictSummary;
        try
        {
            var d = dictionaries.Current;
            dictSummary = $"{d.CategoryByExtension.Count} extensions in {d.CategoryByExtension.Values.Distinct().Count()} categories, " +
                          $"{d.SkipFolders.Count} skipped folders, {d.BusTypes.Count} connection types.";
        }
        catch (RecoveryServiceException ex) { dictSummary = "Not loaded: " + ex.Message; }

        view.ShowSettings(new SettingsViewModel
        {
            Provider = settings.Provider == DatabaseProvider.Sqlite ? "SQLite (local file)" : "SQL Server",
            Connection = settings.SafeConnectionDescription,
            EnvFile = settings.EnvFile ?? "(none found - using defaults; copy .env.example to .env)",
            Dictionaries = dictionaries.Location,
            DictionarySummary = dictSummary,
            LogFolder = log.FilePath,
            Network = settings.Network.IsConfigured
                ? $"Configured: {settings.Network.Protocol.ToUpperInvariant()} {settings.Network.User}@{settings.Network.Host}:{settings.Network.Port} (not used yet)"
                : "Not configured (not needed yet).",
        });
    }

    async void Merge()
    {
        var file = view.PickCatalogFile();
        if (file == null) return;
        if (runner.IsBusy) { main.ShowInfo("Busy", "Another operation is running."); return; }
        MergeResult? result = null;
        var progress = new Progress<ProgressInfo>(p => main.SetProgress(ProgressText.From(p)));
        await runner.RunAsync("Merging", ct => result = merges.Merge(file, progress, ct), ex => main.ShowError("Merge", ex.Message));
        if (result != null) main.ShowInfo("Merge", $"Merged {result.Files:N0} file records from {result.Disks} disk(s).");
    }

    static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
