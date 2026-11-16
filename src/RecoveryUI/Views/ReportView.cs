using RecoveryUI.Infrastructure;
using RecoveryUI.ViewModels;

namespace RecoveryUI.Views;

/// <summary>"How much is unique, how much is a copy, does it fit?" Read-only. Expert mode adds the duplicate groups.</summary>
public partial class ReportView : UserControl, IReportView
{
    public event EventHandler? RefreshRequested;
    public event EventHandler? HashPendingRequested;
    public event EventHandler<int>? DuplicateGroupsRequested;
    public event EventHandler? CheckDuplicatesRequested;
    public Control Control => this;
    bool expert;

    public ReportView()
    {
        InitializeComponent();
        UiTheme.Grid(gridTypes);
        UiTheme.Grid(gridDisks);
        UiTheme.Secondary(btnRefresh);
        UiTheme.Primary(btnHashPending);
        UiTheme.Secondary(btnGroups);
        UiTheme.Secondary(btnCheckDuplicates);
        lblIntro.ForeColor = UiTheme.Muted;
        new ToolTip().SetToolTip(btnCheckDuplicates, "Re-read every copy of 20 random duplicate groups and compare them byte for byte (SHA-256). Read-only.");
        btnCheckDuplicates.Click += (_, _) => CheckDuplicatesRequested?.Invoke(this, EventArgs.Empty);
        lblNeeded.Font = UiTheme.HeadingFont;
        lblPending.ForeColor = UiTheme.Muted;
        tileUnique.ValueColor = UiTheme.Good;
        tileExtra.ValueColor = UiTheme.Warning;
        btnRefresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        btnHashPending.Click += (_, _) => HashPendingRequested?.Invoke(this, EventArgs.Empty);
        btnGroups.Click += (_, _) => DuplicateGroupsRequested?.Invoke(this, (int)numTop.Value);
        SetExpertMode(false);
    }

    public void ShowReport(ReportViewModel vm) => this.OnUi(() =>
    {
        tileTotal.Value = vm.Total;
        tileTotal.Caption = vm.TotalCaption;
        tileUnique.Value = vm.Unique;
        tileExtra.Value = vm.Extra;
        tilePending.Value = vm.Pending;
        lblNeeded.Text = vm.Needed;
        lblPending.Text = vm.PendingHint;
        btnHashPending.Visible = vm.CanHashPending;
        GridBinder.Bind(gridTypes, vm.Types, expert);
        GridBinder.Bind(gridDisks, vm.Disks, expert);
    });

    public void ShowDuplicateGroups(IReadOnlyList<DuplicateGroupViewModel> groups) => this.OnUi(() =>
    {
        treeGroups.BeginUpdate();
        treeGroups.Nodes.Clear();
        foreach (var g in groups)
        {
            var node = treeGroups.Nodes.Add(g.Title);
            foreach (var c in g.Copies) node.Nodes.Add(c);
        }
        if (groups.Count == 0) treeGroups.Nodes.Add("No duplicates found (yet).");
        treeGroups.EndUpdate();
    });

    public void SetExpertMode(bool expert)
    {
        this.expert = expert;
        if (expert && !tabs.TabPages.Contains(tabGroups)) tabs.TabPages.Add(tabGroups);
        if (!expert && tabs.TabPages.Contains(tabGroups)) tabs.TabPages.Remove(tabGroups);
        GridBinder.ApplyExpert<DiskSummaryRowViewModel>(gridDisks, expert);
    }
}
