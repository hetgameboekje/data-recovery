using RecoveryBLL.Services;
using RecoveryUI.Infrastructure;
using RecoveryUI.ViewModels;
using RecoveryUI.Views;

namespace RecoveryUI.Controllers;

/// <summary>Navigation, Simple/Expert mode, the shared busy state and the live log.</summary>
public sealed class MainController(
    IMainView main, OperationRunner runner, UiLog log, CatalogService catalog,
    HomeController home, DisksController disks, ScanController scan, ReportController report, SettingsController settings)
{
    readonly Dictionary<Page, IPageController> pages = new()
    {
        [Page.Home] = home, [Page.Disks] = disks, [Page.Scan] = scan, [Page.Report] = report, [Page.Settings] = settings,
    };
    bool expert;

    public void Start()
    {
        home.Attach();
        disks.Attach();
        scan.Attach();
        report.Attach();
        settings.Attach();

        main.NavigateRequested += (_, p) => Navigate(p);
        main.ExpertModeChanged += (_, on) => SetExpert(on);
        main.CancelRequested += (_, _) => runner.Cancel();
        home.NavigateRequested += (_, p) => Navigate(p);
        disks.LinuxScanRequested += (_, r) => { Navigate(Page.Scan); scan.StartLinux(r); };
        runner.BusyChanged += (busy, name) =>
        {
            main.SetBusy(busy, name);
            if (!busy) pages[Page.Home].OnShown();   // keep the home numbers fresh
        };
        log.LineWritten += main.AppendLog;

        // Create every page's window handle now (UI thread), so controllers can update pages from worker threads.
        foreach (var c in pages.Values) _ = c.View.Control.Handle;

        main.SetCatalogDescription("Catalog: " + catalog.Description);
        SetExpert(UiPreferences.LoadExpert());
        Navigate(Page.Home);
    }

    void Navigate(Page page)
    {
        var c = pages[page];
        main.ShowPage(c.View, page, c.Title);
        c.OnShown();
    }

    void SetExpert(bool on)
    {
        expert = on;
        main.SetExpertMode(on);
        foreach (var c in pages.Values) c.View.SetExpertMode(on);
        UiPreferences.SaveExpert(on);
    }
}

/// <summary>Remembers Simple/Expert between runs (a per-user convenience; nothing important).</summary>
static class UiPreferences
{
    static readonly string File = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RecoveryToolkit", "ui-expert-mode");

    public static bool LoadExpert()
    {
        try { return System.IO.File.Exists(File) && System.IO.File.ReadAllText(File).Trim() == "1"; }
        catch (IOException) { return false; }
    }

    public static void SaveExpert(bool on)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            System.IO.File.WriteAllText(File, on ? "1" : "0");
        }
        catch (IOException) { }
    }
}
