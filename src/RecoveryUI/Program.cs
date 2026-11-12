using Microsoft.Extensions.DependencyInjection;
using RecoveryBLL;
using RecoveryBLL.Exceptions;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;
using RecoveryBLL.Services;
using RecoveryDAL;
using RecoveryDAL.Configuration;
using RecoveryUI.Controllers;
using RecoveryUI.Infrastructure;
using RecoveryUI.Views;

namespace RecoveryUI;

static class Program
{
    /// <summary>Composition root: the only place that knows every layer. Desktop app, so no login.</summary>
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            MessageBox.Show(e.Exception.Message, "Unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);

        AppSettings settings;
        try
        {
            settings = EnvSettingsLoader.Load();
        }
        catch (ConfigurationException ex)
        {
            MessageBox.Show(ex.Message + "\n\nFix the .env file (see .env.example) and start again.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var log = new UiLog(settings.LogDirectory);
        using var services = new ServiceCollection()
            .AddRecoveryDal(settings)
            .AddRecoveryBll()
            .AddSingleton<IOperationLog>(log)
            .AddSingleton(log)
            .AddSingleton<OperationRunner>()
            .AddSingleton<IDeletionConfirmer, DeletionDialogs>()
            // views (one instance each, kept alive while switching pages)
            .AddSingleton<MainForm>().AddSingleton<IMainView>(sp => sp.GetRequiredService<MainForm>())
            .AddSingleton<IHomeView, HomeView>()
            .AddSingleton<IDisksView, DisksView>()
            .AddSingleton<IScanView, ScanView>()
            .AddSingleton<IReportView, ReportView>()
            .AddSingleton<IDiskMapView, DiskMapView>()
            .AddSingleton<IJunkView, JunkView>()
            .AddSingleton<ICopyView, CopyView>()
            .AddSingleton<ISettingsView, SettingsView>()
            // controllers
            .AddSingleton<HomeController>()
            .AddSingleton<DisksController>()
            .AddSingleton<ScanController>()
            .AddSingleton<ReportController>()
            .AddSingleton<DiskMapController>()
            .AddSingleton<JunkController>()
            .AddSingleton<CopyController>()
            .AddSingleton<SettingsController>()
            .AddSingleton<MainController>()
            .BuildServiceProvider();

        try
        {
            services.GetRequiredService<CatalogService>().Initialize();
        }
        catch (RecoveryServiceException ex)
        {
            MessageBox.Show(ex.Message, "Catalog database", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Give back what a crashed or killed run left behind: Linux disks are unmounted (written to the log).
        // Half-written copies are only deleted after both steps (two dialogs, both defaulting to "No").
        try
        {
            var runState = services.GetRequiredService<RunStateService>();
            var cleanup = runState.ReleaseStale();
            if (cleanup.Problems.Count > 0)
                MessageBox.Show(string.Join("\n\n", cleanup.Problems), "Left behind by an earlier run", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (!cleanup.Leftovers.IsEmpty
                && DeletionDialogs.ApproveList(cleanup.Leftovers, "An interrupted copy left half-written files behind. They are harmless, but take space."))
                runState.DeleteLeftovers(null, cleanup.Leftovers.Code);
        }
        catch (RecoveryServiceException ex)
        {
            log.Warn(ex.Message);
        }

        var form = services.GetRequiredService<MainForm>();
        form.Load += (_, _) => services.GetRequiredService<MainController>().Start();
        Application.Run(form);
    }
}
