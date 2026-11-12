using RecoveryBLL.Models;
using RecoveryUI.Infrastructure;
using RecoveryUI.ViewModels;

namespace RecoveryUI.Views;

/// <summary>
/// Phase 2 in three steps: 1 choose the destinations (order = main, then overflow), 2 check the plan (dry run),
/// 3 copy. Simple mode shows the tiles and one sentence; Expert mode adds options, caps, CSV export and more columns.
/// </summary>
public partial class CopyView : UserControl, ICopyView
{
    public event EventHandler? PlanRequested;
    public event EventHandler? StartRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? ReviewDeletionsRequested;
    public event EventHandler? ExportRequested;
    public event EventHandler? RefreshDrivesRequested;
    public event EventHandler? SettingsChanged;
    public Control Control => this;

    sealed record DriveChoice(string Root, string Text)
    {
        public override string ToString() => Text;
    }

    bool expert, running, planShown, suppressCap;

    static readonly (JunkHandling Value, string Text)[] JunkChoices =
    [
        (JunkHandling.SetAside, "Put aside in _Review (after your own files, if there is room)"),
        (JunkHandling.Skip, "Leave them out (they stay on the source disks)"),
        (JunkHandling.Off, "Don't detect junk: sort everything like your own files"),
    ];

    public CopyView()
    {
        InitializeComponent();
        foreach (var l in new[] { lblStep1, lblStep2, lblStep3, lblIntroTitle }) l.Font = UiTheme.HeadingFont;
        lblStep1Hint.ForeColor = lblStep2Hint.ForeColor = lblNotes.ForeColor = lblDetail.ForeColor = lblIntroDeep.ForeColor = UiTheme.Muted;
        panelIntro.BackColor = UiTheme.Card;
        lnkIntro.LinkColor = UiTheme.Accent;
        lnkIntro.LinkClicked += (_, _) => ShowIntro(!lblIntro.Visible, remember: true);
        ShowIntro(!UiPreferences.Get("copy-intro-hidden"), remember: false);
        lblIntroExample.Font = new Font("Consolas", 9.5f);
        lblSafe.ForeColor = UiTheme.Good;
        lblVerdict.Font = UiTheme.HeadingFont;
        lblStage.Font = UiTheme.HeadingFont;
        lblDeletions.ForeColor = UiTheme.Warning;
        tileUnique.ValueColor = UiTheme.Good;
        tileDuplicates.ValueColor = UiTheme.Warning;
        tileReview.ValueColor = UiTheme.Muted;
        cmbJunk.Items.AddRange(JunkChoices.Select(c => (object)c.Text).ToArray());
        cmbJunk.SelectedIndex = 0;
        cmbJunk.SelectedIndexChanged += (_, _) => Changed();
        foreach (var b in new[] { btnAddDrive, btnAddFolder, btnUp, btnDown, btnRemove, btnRefreshDrives, btnExport, btnReviewDeletions, btnStop })
            UiTheme.Secondary(b);
        UiTheme.Primary(btnPlan);
        UiTheme.Primary(btnStart);
        UiTheme.Grid(gridDestinations);

        btnAddDrive.Click += (_, _) => AddFromBox();
        btnAddFolder.Click += (_, _) => AddFolder();
        btnUp.Click += (_, _) => MoveSelected(-1);
        btnDown.Click += (_, _) => MoveSelected(+1);
        btnRemove.Click += (_, _) =>
        {
            if (lstDestinations.SelectedIndex < 0) return;
            lstDestinations.Items.RemoveAt(lstDestinations.SelectedIndex);
            Changed();
        };
        btnRefreshDrives.Click += (_, _) => RefreshDrivesRequested?.Invoke(this, EventArgs.Empty);
        btnPlan.Click += (_, _) => PlanRequested?.Invoke(this, EventArgs.Empty);
        btnExport.Click += (_, _) => ExportRequested?.Invoke(this, EventArgs.Empty);
        btnReviewDeletions.Click += (_, _) => ReviewDeletionsRequested?.Invoke(this, EventArgs.Empty);
        btnStart.Click += (_, _) => StartRequested?.Invoke(this, EventArgs.Empty);
        btnStop.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
        foreach (var c in new[] { chkDuplicates, chkVerify, chkWait, chkAllowPending }) c.CheckedChanged += (_, _) => Changed();
        numReserve.ValueChanged += (_, _) => Changed();
        lstDestinations.SelectedIndexChanged += (_, _) => ShowCap();
        numCap.ValueChanged += (_, _) => SetCap();

        btnStop.Visible = false;          // direct: the handle does not exist yet, so OnUi would skip it
        panelPlan.Visible = false;
        panelProgress.Visible = false;
        SetExpertMode(false);
        UpdateGuidance();
    }

    public IReadOnlyList<DestinationChoiceViewModel> Destinations => lstDestinations.Items.Cast<DestinationChoiceViewModel>().ToList();

    public CopyOptionsViewModel Options => new()
    {
        CopyDuplicates = chkDuplicates.Checked,
        Verify = chkVerify.Checked,
        WaitForUnplugged = chkWait.Checked,
        AllowPending = chkAllowPending.Checked,
        Junk = JunkChoices[Math.Max(0, cmbJunk.SelectedIndex)].Value,
        ReserveBytes = (long)numReserve.Value << 30,
    };

    public void ShowDrives(IReadOnlyList<DriveRowViewModel> drives) => this.OnUi(() =>
    {
        var selected = (cmbDrives.SelectedItem as DriveChoice)?.Root;
        cmbDrives.Items.Clear();
        foreach (var d in drives) cmbDrives.Items.Add(new DriveChoice(d.Root, $"{d.Drive}  {d.Label} — {d.Free} free of {d.Size}, {d.Connection}"));
        var again = cmbDrives.Items.Cast<DriveChoice>().FirstOrDefault(d => d.Root == selected);
        if (again != null) cmbDrives.SelectedItem = again;
        else if (cmbDrives.Items.Count > 0) cmbDrives.SelectedIndex = 0;
    });

    void Add(string path, string text)
    {
        if (Destinations.Any(d => string.Equals(d.Path.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) return;
        var role = lstDestinations.Items.Count == 0 ? "Main" : "Overflow";
        lstDestinations.Items.Add(new DestinationChoiceViewModel(path, $"{role}: {text}", null));
        Renumber();
        Changed();
    }

    /// <summary>A drive picked from the list, or a folder path typed/pasted into the box (checked by the plan, like any destination).</summary>
    void AddFromBox()
    {
        if (cmbDrives.SelectedItem is DriveChoice d && cmbDrives.Text == d.Text) { Add(d.Root, d.Text); return; }
        var typed = cmbDrives.Text.Trim().Trim('"');
        if (typed.Length == 0) return;
        if (typed.Length == 2 && typed[1] == ':') typed += "\\";
        Add(typed, typed);
        cmbDrives.Text = "";
    }

    void AddFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Choose the backup folder (on the Windows drive: a subfolder such as C:\\Recovery)",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) Add(dlg.SelectedPath, dlg.SelectedPath);
    }

    /// <summary>The explanation can be folded away once read, so the steps move up (remembered per user).</summary>
    void ShowIntro(bool show, bool remember)
    {
        lblIntro.Visible = lblIntroExample.Visible = lblIntroDeep.Visible = show;
        lnkIntro.Text = show ? "Hide explanation" : "Show explanation";
        if (remember) UiPreferences.Set("copy-intro-hidden", !show);
    }

    void MoveSelected(int delta)
    {
        var i = lstDestinations.SelectedIndex;
        var j = i + delta;
        if (i < 0 || j < 0 || j >= lstDestinations.Items.Count) return;
        var item = lstDestinations.Items[i];
        lstDestinations.Items.RemoveAt(i);
        lstDestinations.Items.Insert(j, item);
        Renumber();
        lstDestinations.SelectedIndex = j;
        Changed();
    }

    /// <summary>The first destination is the main disk, the others overflow: keep the labels right after moving.</summary>
    void Renumber()
    {
        for (int i = 0; i < lstDestinations.Items.Count; i++)
        {
            var d = (DestinationChoiceViewModel)lstDestinations.Items[i];
            var text = d.Text[(d.Text.IndexOf(": ", StringComparison.Ordinal) + 2)..];
            lstDestinations.Items[i] = d with { Text = $"{(i == 0 ? "Main" : "Overflow")}: {text}" };
        }
    }

    void ShowCap()
    {
        suppressCap = true;
        numCap.Enabled = lstDestinations.SelectedIndex >= 0;
        numCap.Value = lstDestinations.SelectedItem is DestinationChoiceViewModel { MaxBytes: { } m } ? m >> 30 : 0;
        suppressCap = false;
    }

    void SetCap()
    {
        if (suppressCap || lstDestinations.SelectedIndex < 0) return;
        var i = lstDestinations.SelectedIndex;
        var d = (DestinationChoiceViewModel)lstDestinations.Items[i];
        lstDestinations.Items[i] = d with { MaxBytes = numCap.Value > 0 ? (long)numCap.Value << 30 : null };
        lstDestinations.SelectedIndex = i;
        Changed();
    }

    void Changed()
    {
        if (running) return;
        UpdateGuidance();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Only the next sensible step is clickable: no plan without a destination, no copy without a plan.</summary>
    void UpdateGuidance()
    {
        bool hasDestination = lstDestinations.Items.Count > 0;
        btnPlan.Enabled = hasDestination && !running;
        lblStep2Hint.Text = hasDestination
            ? "Press \"Check the plan\": it shows how much will be copied, whether it fits, and where it goes. Nothing is written. You can do this as often as you like."
            : "First add a backup disk in step 1.";
    }

    public void ShowPlan(CopyPlanViewModel? plan) => this.OnUi(() =>
    {
        planShown = plan != null;
        panelPlan.Visible = planShown;
        btnExport.Visible = expert && planShown;
        btnStart.Enabled = plan?.CanStart == true && !running;
        if (plan == null) return;
        if (!running) panelProgress.Visible = false;   // the preview's own progress is done; step 3 shows the copy's
        tileToCopy.Value = plan.ToCopy;
        tileToCopy.Caption = plan.ToCopyCaption;
        tileUnique.Value = plan.Unique;
        tileDuplicates.Value = plan.Duplicates;
        tileDuplicates.Caption = plan.DuplicatesCaption;
        tileReview.Value = plan.Review;
        tileReview.Caption = plan.ReviewCaption;
        tileAlready.Value = plan.Already;
        tileAlready.Caption = plan.AlreadyCaption;
        lblVerdict.Text = plan.Verdict;
        lblVerdict.ForeColor = plan.Fits ? UiTheme.Good : UiTheme.Warning;
        GridBinder.Bind(gridDestinations, plan.Destinations, expert);
        lblNotes.Text = string.Join(Environment.NewLine, plan.Notes.Select(n => "• " + n));
        lblNotes.Visible = plan.Notes.Count > 0;
        panelDeletions.Visible = plan.HasDeletions;
        lblDeletions.Text = plan.Deletions;
        lblDeletions.ForeColor = UiTheme.Warning;
    });

    public void SetDeletionApproval(string text, bool approved) => this.OnUi(() =>
    {
        lblDeletions.Text = text;
        lblDeletions.ForeColor = approved ? UiTheme.Good : UiTheme.Warning;
    });

    public void SetRunning(bool on) => this.OnUi(() =>
    {
        running = on;
        btnStop.Visible = on;
        UpdateGuidance();
        btnStart.Enabled = !on && planShown;
        panelDest.Enabled = groupOptions.Enabled = btnReviewDeletions.Enabled = !on;
        if (on) { lblResult.Text = ""; panelProgress.Visible = true; }
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
        if (success) progress.Value = 100;
    });

    public string? PickCsvFile()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "Save the copy plan", Filter = "CSV (semicolon-separated)|*.csv", FileName = $"copy-plan-{DateTime.Now:yyyyMMdd-HHmm}.csv",
        };
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileName : null;
    }

    public bool Confirm(string title, string message) =>
        MessageBox.Show(this, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) == DialogResult.Yes;

    public void SetExpertMode(bool on)
    {
        expert = on;
        groupOptions.Visible = on;
        btnExport.Visible = on && planShown;
        GridBinder.ApplyExpert<DestinationRowViewModel>(gridDestinations, on);
    }
}
