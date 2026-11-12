using RecoveryUI.Infrastructure;
using RecoveryUI.ViewModels;

namespace RecoveryUI.Views;

/// <summary>Pick drives/folders, start cataloguing, watch progress. Expert mode adds the tuning options.</summary>
public partial class ScanView : UserControl, IScanView
{
    public event EventHandler? StartRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? RefreshRequested;
    public Control Control => this;

    sealed record Choice(string Path, string Text)
    {
        public override string ToString() => Text;
    }

    public ScanView()
    {
        InitializeComponent();
        foreach (var l in new[] { lblStep1, lblStep2 }) l.Font = UiTheme.HeadingFont;
        lblSafe.ForeColor = UiTheme.Good;
        lblStage.Font = UiTheme.HeadingFont;
        lblDetail.ForeColor = UiTheme.Muted;
        UiTheme.Secondary(btnAddFolder);
        UiTheme.Secondary(btnRefresh);
        UiTheme.Primary(btnStart);
        UiTheme.Secondary(btnStop);
        cmbReaders.Items.AddRange(["Automatic (recommended)", "1", "2", "4", "8", "16"]);
        cmbReaders.SelectedIndex = 0;
        btnStart.Click += (_, _) => StartRequested?.Invoke(this, EventArgs.Empty);
        btnStop.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
        btnRefresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        btnAddFolder.Click += (_, _) => AddFolder();
        btnStop.Visible = false;          // direct: the handle does not exist yet, so OnUi would skip it
        panelProgress.Visible = false;
        SetExpertMode(false);
    }

    public IReadOnlyList<string> SelectedTargets => lstTargets.CheckedItems.Cast<Choice>().Select(c => c.Path).ToList();

    public ScanOptionsViewModel Options => new()
    {
        ReadersPerDisk = cmbReaders.SelectedIndex <= 0 ? 0 : int.Parse((string)cmbReaders.SelectedItem!),
        Sha256 = chkSha256.Checked,
        ReadImageInfo = chkImageInfo.Checked,
        ListOnly = chkListOnly.Checked,
        Excludes = txtExcludes.Text.Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
    };

    public void ShowDrives(IReadOnlyList<DriveRowViewModel> drives) => this.OnUi(() =>
    {
        var checkedPaths = SelectedTargets.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var folders = lstTargets.Items.Cast<Choice>().Where(c => !drives.Any(d => d.Root == c.Path) && !c.Path.EndsWith(":\\")).ToList();
        lstTargets.Items.Clear();
        foreach (var d in drives) lstTargets.Items.Add(new Choice(d.Root, d.ChoiceText), checkedPaths.Contains(d.Root));
        foreach (var f in folders) lstTargets.Items.Add(f, checkedPaths.Contains(f.Path));
    });

    void AddFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "Choose a folder to catalog", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        lstTargets.Items.Add(new Choice(dlg.SelectedPath, "Folder: " + dlg.SelectedPath), true);
    }

    public void SetRunning(bool running) => this.OnUi(() =>
    {
        btnStart.Enabled = !running;
        btnStop.Visible = running;
        lstTargets.Enabled = btnAddFolder.Enabled = btnRefresh.Enabled = groupOptions.Enabled = !running;
        panelProgress.Visible = running || lblResult.Text.Length > 0;
        if (running) lblResult.Text = "";
    });

    public void ShowProgress(ProgressViewModel p) => this.OnUi(() =>
    {
        panelProgress.Visible = true;
        lblStage.Text = p.Stage;
        lblDetail.Text = p.Detail;
        progress.Style = p.Indeterminate ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        if (!p.Indeterminate) progress.Value = Math.Clamp(p.Percent, 0, 100);
    });

    public void ShowResult(string text, bool success) => this.OnUi(() =>
    {
        panelProgress.Visible = true;
        lblResult.ForeColor = success ? UiTheme.Good : UiTheme.Warning;
        lblResult.Text = text;
        progress.Style = ProgressBarStyle.Continuous;
        progress.Value = success ? 100 : progress.Value;
    });

    public void SetExpertMode(bool expert) => groupOptions.Visible = expert;
}
