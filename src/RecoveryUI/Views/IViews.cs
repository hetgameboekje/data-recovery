using RecoveryUI.ViewModels;

namespace RecoveryUI.Views;

// View interfaces: controllers only talk to these, so views can be replaced (or faked in tests)
// without touching the controllers or the BLL.

public interface IPageView
{
    Control Control { get; }
    void SetExpertMode(bool expert);
}

public interface IMainView
{
    event EventHandler<Page>? NavigateRequested;
    event EventHandler<bool>? ExpertModeChanged;
    event EventHandler? CancelRequested;
    void ShowPage(IPageView page, Page which, string title);
    void SetExpertMode(bool expert);
    void SetCatalogDescription(string text);
    void SetBusy(bool busy, string? operation);
    void SetProgress(ProgressViewModel? progress);
    void AppendLog(string line, bool warn);
    void ShowError(string title, string message);
    void ShowInfo(string title, string message);
}

public interface IHomeView : IPageView
{
    event EventHandler<Page>? StepClicked;
    void ShowSummary(HomeSummaryViewModel vm);
}

public sealed record LinuxScanRequest(int DiskNumber, int Partition, string FileSystem);

public interface IDisksView : IPageView
{
    event EventHandler? RefreshRequested;
    event EventHandler<LinuxScanRequest>? LinuxScanRequested;
    void ShowDrives(IReadOnlyList<DriveRowViewModel> drives);
    void ShowCatalogued(IReadOnlyList<CatalogDiskRowViewModel> disks);
    void ShowDevices(IReadOnlyList<DeviceRowViewModel> devices);
    void ShowLinuxStatus(string status, bool canMount);
}

public interface IScanView : IPageView
{
    event EventHandler? StartRequested;
    event EventHandler? StopRequested;
    event EventHandler? RefreshRequested;
    IReadOnlyList<string> SelectedTargets { get; }
    ScanOptionsViewModel Options { get; }
    void ShowDrives(IReadOnlyList<DriveRowViewModel> drives);
    void SetRunning(bool running);
    void ShowProgress(ProgressViewModel progress);
    void ShowResult(string text, bool success);
}

public interface IReportView : IPageView
{
    event EventHandler? RefreshRequested;
    event EventHandler? HashPendingRequested;
    event EventHandler<int>? DuplicateGroupsRequested;
    void ShowReport(ReportViewModel vm);
    void ShowDuplicateGroups(IReadOnlyList<DuplicateGroupViewModel> groups);
}

public interface ISettingsView : IPageView
{
    event EventHandler? ReloadDictionariesRequested;
    event EventHandler? OpenLogFolderRequested;
    event EventHandler? OpenConfigFolderRequested;
    event EventHandler? MergeRequested;
    void ShowSettings(SettingsViewModel vm);
    string? PickCatalogFile();
}
