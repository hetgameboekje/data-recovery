using RecoveryUI.Infrastructure;
using RecoveryUI.ViewModels;

namespace RecoveryUI.Views;

/// <summary>Where things are stored. Settings themselves live in the .env file and dictionaries.xml.</summary>
public partial class SettingsView : UserControl, ISettingsView
{
    public event EventHandler? ReloadDictionariesRequested;
    public event EventHandler? OpenLogFolderRequested;
    public event EventHandler? OpenConfigFolderRequested;
    public event EventHandler? MergeRequested;
    public Control Control => this;

    public SettingsView()
    {
        InitializeComponent();
        foreach (var l in new[] { lblDbTitle, lblDictTitle, lblLogTitle, lblNetTitle }) l.Font = UiTheme.HeadingFont;
        foreach (var l in new[] { lblDbHelp, lblDictHelp, lblNetHelp }) l.ForeColor = UiTheme.Muted;
        foreach (var b in new[] { btnOpenConfig, btnMerge, btnReload, btnOpenLogs }) UiTheme.Secondary(b);
        btnReload.Click += (_, _) => ReloadDictionariesRequested?.Invoke(this, EventArgs.Empty);
        btnOpenLogs.Click += (_, _) => OpenLogFolderRequested?.Invoke(this, EventArgs.Empty);
        btnOpenConfig.Click += (_, _) => OpenConfigFolderRequested?.Invoke(this, EventArgs.Empty);
        btnMerge.Click += (_, _) => MergeRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ShowSettings(SettingsViewModel vm) => this.OnUi(() =>
    {
        lblDb.Text = $"{vm.Provider}\n{vm.Connection}\nSettings file: {vm.EnvFile}";
        lblDict.Text = $"{vm.Dictionaries}\n{vm.DictionarySummary}";
        lblLog.Text = vm.LogFolder;
        lblNet.Text = vm.Network;
    });

    public string? PickCatalogFile()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Choose a catalog made on another PC",
            Filter = "Catalog (*.db)|*.db|All files (*.*)|*.*",
        };
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileName : null;
    }

    public void SetExpertMode(bool expert) { /* same in both modes */ }
}
