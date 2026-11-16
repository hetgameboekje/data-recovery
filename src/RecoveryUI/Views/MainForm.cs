using RecoveryUI.Infrastructure;
using RecoveryUI.ViewModels;

namespace RecoveryUI.Views;

/// <summary>
/// Shell: navigation on the left, the current page in the middle, a status bar at the bottom.
/// Simple mode shows the guided workflow; Expert mode adds technical columns, options and the live log.
/// </summary>
public partial class MainForm : Form, IMainView
{
    public event EventHandler<Page>? NavigateRequested;
    public event EventHandler<bool>? ExpertModeChanged;
    public event EventHandler? CancelRequested;

    readonly Dictionary<Page, Button> navButtons;
    bool suppressExpertEvent;

    public MainForm()
    {
        InitializeComponent();
        navButtons = new()
        {
            [Page.Home] = btnHome, [Page.Disks] = btnDisks, [Page.Scan] = btnScan,
            [Page.Report] = btnReport, [Page.DiskMap] = btnDiskMap, [Page.Junk] = btnJunk, [Page.Cleanup] = btnCleanup, [Page.Copy] = btnCopy, [Page.Settings] = btnSettings,
        };
        ApplyTheme();
        foreach (var (page, button) in navButtons)
            button.Click += (_, _) => NavigateRequested?.Invoke(this, page);
        chkExpert.CheckedChanged += (_, _) =>
        {
            chkExpert.Text = chkExpert.Checked ? "Expert mode: on" : "Expert mode: off";
            if (!suppressExpertEvent) ExpertModeChanged?.Invoke(this, chkExpert.Checked);
        };
        btnCancel.Click += (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);
        var tips = new ToolTip(components!);
        foreach (var b in new[] { btnFutureCorrupt, btnFutureDeep, btnFutureNetwork })
            tips.SetToolTip(b, (string)b.Tag!);
        tips.SetToolTip(chkExpert, "Show technical details: disk numbers, bus types, readers per disk, hashes, the live log and Linux (WSL) disks.");
    }

    void ApplyTheme()
    {
        BackColor = UiTheme.Surface;
        panelNav.BackColor = UiTheme.Sidebar;
        lblApp.Font = UiTheme.HeadingFont;
        lblApp.ForeColor = UiTheme.Accent;
        lblComing.ForeColor = UiTheme.Muted;
        lblPageTitle.Font = UiTheme.TitleFont;
        lblPageTitle.ForeColor = UiTheme.Text;
        panelHeader.BackColor = UiTheme.Surface;
        foreach (var b in navButtons.Values) StyleNav(b, false);
        foreach (var b in new[] { btnFutureCorrupt, btnFutureDeep, btnFutureNetwork })
        {
            StyleNav(b, false);
            b.ForeColor = UiTheme.Muted;
        }
        UiTheme.Secondary(btnCancel);
        txtLog.BackColor = UiTheme.Card;
        txtLog.ForeColor = UiTheme.Text;
    }

    static void StyleNav(Button b, bool selected)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = UiTheme.SidebarSelected;
        b.BackColor = selected ? UiTheme.SidebarSelected : UiTheme.Sidebar;
        b.ForeColor = selected ? UiTheme.Accent : UiTheme.Text;
        b.Font = selected ? new Font("Segoe UI Semibold", 10f) : new Font("Segoe UI", 10f);
        b.TextAlign = ContentAlignment.MiddleLeft;
        b.Cursor = Cursors.Hand;
    }

    /// <summary>
    /// With display scaling (125–175 %, usual on laptops) the designed size can be taller than the screen, which hides the
    /// status bar with its Stop button. Never open larger than the screen: maximise instead, otherwise centre.
    /// </summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var area = Screen.FromControl(this).WorkingArea;
        if (Width > area.Width || Height > area.Height)
            WindowState = FormWindowState.Maximized;
        else
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
    }

    public void ShowPage(IPageView page, Page which, string title)
    {
        lblPageTitle.Text = title;
        foreach (var (p, b) in navButtons) StyleNav(b, p == which);
        if (panelContent.Controls.Count == 1 && panelContent.Controls[0] == page.Control) return;
        panelContent.SuspendLayout();
        panelContent.Controls.Clear();
        page.Control.Dock = DockStyle.Fill;
        panelContent.Controls.Add(page.Control);
        panelContent.ResumeLayout();
    }

    public void SetExpertMode(bool expert)
    {
        suppressExpertEvent = true;
        chkExpert.Checked = expert;
        suppressExpertEvent = false;
        panelLog.Visible = expert;
    }

    public void SetCatalogDescription(string text) => this.OnUi(() => lblCatalog.Text = text);

    public void SetBusy(bool busy, string? operation) => this.OnUi(() =>
    {
        btnCancel.Visible = busy;
        progressBar.Visible = busy;
        lblStatus.Text = busy ? operation + "…" : "Ready";
        if (!busy) progressBar.Value = 0;
    });

    public void SetProgress(ProgressViewModel? p) => this.OnUi(() =>
    {
        if (p == null) return;
        progressBar.Style = p.Indeterminate ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        if (!p.Indeterminate) progressBar.Value = Math.Clamp(p.Percent, 0, 100);
        lblStatus.Text = $"{p.Stage}: {p.Detail}";
    });

    public void AppendLog(string line, bool warn) => this.OnUi(() =>
    {
        if (txtLog.TextLength > 200_000) txtLog.Clear();
        txtLog.AppendText($"{DateTime.Now:HH:mm:ss}  {(warn ? "⚠ " : "")}{line}{Environment.NewLine}");
    });

    public void ShowError(string title, string message) =>
        this.OnUi(() => MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning));

    public void ShowInfo(string title, string message) =>
        this.OnUi(() => MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information));

    /// <summary>Yes/No, "No" by default. Call on the UI thread.</summary>
    public bool Confirm(string title, string message) =>
        MessageBox.Show(this, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
}
