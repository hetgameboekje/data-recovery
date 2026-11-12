using RecoveryUI.Infrastructure;
using RecoveryUI.ViewModels;

namespace RecoveryUI.Views;

/// <summary>
/// Phase 3: junk and keep-together folders. Read-only: three tiles, what was found per reason, the folders kept whole,
/// and the largest files of the selected junk reason (right-click: Show in Explorer / Copy path).
/// </summary>
public partial class JunkView : UserControl, IJunkView
{
    public event EventHandler? RefreshRequested;
    public event EventHandler? OpenRulesRequested;
    public event EventHandler<(long DiskId, string Path, bool IsFile)>? OpenInExplorerRequested;
    public Control Control => this;

    JunkPageViewModel? vm;
    bool expert;

    public JunkView()
    {
        InitializeComponent();
        lblIntro.ForeColor = UiTheme.Muted;
        foreach (var l in new[] { lblGroups, lblBundles, lblExamples }) l.ForeColor = UiTheme.Muted;
        UiTheme.Primary(btnRefresh);
        UiTheme.Secondary(btnRules);
        foreach (var g in new[] { gridGroups, gridBundles, gridExamples }) UiTheme.Grid(g);
        tileJunk.ValueColor = UiTheme.Warning;
        tileKept.ValueColor = UiTheme.Good;
        tileReview.ValueColor = UiTheme.Muted;

        btnRefresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        btnRules.Click += (_, _) => OpenRulesRequested?.Invoke(this, EventArgs.Empty);
        gridGroups.SelectionChanged += (_, _) => ShowExamples();
        gridGroups.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && GridBinder.Selected<JunkGroupRowViewModel>(gridGroups) is { } g && vm?.Examples.ContainsKey(g.Reason) == true)
                tabs.SelectedTab = tabExamples;
        };
        menuOpen.Click += (_, _) => { if (Target() is { } t) OpenInExplorerRequested?.Invoke(this, t); };
        menuCopyPath.Click += (_, _) =>
        {
            if (gridExamples.Focused && GridBinder.Selected<JunkExampleRowViewModel>(gridExamples) is { } e) Clipboard.SetText(e.Path);
            else if (GridBinder.Selected<BundleRowViewModel>(gridBundles) is { } b) Clipboard.SetText(b.Folder);
        };
        // Right-click selects the row under the mouse first, so the menu acts on it.
        foreach (var g in new[] { gridBundles, gridExamples })
            g.CellMouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
                g.Focus();
                g.ClearSelection();
                g.Rows[e.RowIndex].Selected = true;
            };
    }

    (long, string, bool)? Target()
    {
        if (gridExamples.Focused && GridBinder.Selected<JunkExampleRowViewModel>(gridExamples) is { } e) return (e.DiskId, e.RelativePath, true);
        if (GridBinder.Selected<BundleRowViewModel>(gridBundles) is { } b) return (b.DiskId, b.Root, false);
        return null;
    }

    public void ShowLoading(string text) => this.OnUi(() =>
    {
        lblSummary.Text = text;
        lblSummary.ForeColor = UiTheme.Text;
        btnRefresh.Enabled = false;
    });

    public void ShowError(string text) => this.OnUi(() =>
    {
        lblSummary.Text = text;
        lblSummary.ForeColor = UiTheme.Warning;
        btnRefresh.Enabled = true;
    });

    public void ShowResult(JunkPageViewModel result) => this.OnUi(() =>
    {
        vm = result;
        btnRefresh.Enabled = true;
        lblSummary.Text = result.Summary;
        lblSummary.ForeColor = UiTheme.Text;
        (tileJunk.Value, tileJunk.Caption) = (result.JunkSize, result.JunkCaption);
        (tileKept.Value, tileKept.Caption) = (result.KeptSize, result.KeptCaption);
        (tileReview.Value, tileReview.Caption) = (result.ReviewSize, result.ReviewCaption);
        GridBinder.Bind(gridGroups, result.Groups, expert);
        GridBinder.Bind(gridBundles, result.Bundles, expert);
        Widen(gridGroups, "GoesTo");
        Widen(gridBundles, "Folder", "GoesTo");
        var firstJunk = result.Groups.Select((g, i) => (g, i)).FirstOrDefault(x => result.Examples.ContainsKey(x.g.Reason));
        gridGroups.ClearSelection();
        if (firstJunk.g != null) gridGroups.Rows[firstJunk.i].Selected = true;
        ShowExamples();
    });

    void ShowExamples()
    {
        if (vm == null) return;
        var reason = GridBinder.Selected<JunkGroupRowViewModel>(gridGroups)?.Reason;
        if (reason != null && vm.Examples.TryGetValue(reason, out var rows))
        {
            lblExamples.Text = $"{reason}: the {rows.Count:N0} largest files. Right-click one to show it in Explorer.";
            tabExamples.Text = $"Largest junk files: {reason}";
            GridBinder.Bind(gridExamples, rows, expert);
            Widen(gridExamples, "Path");
        }
        else
        {
            lblExamples.Text = "Choose a junk row on the first tab.";
            tabExamples.Text = "Largest junk files";
            GridBinder.Bind(gridExamples, Array.Empty<JunkExampleRowViewModel>(), expert);
        }
    }

    /// <summary>Paths get the room; numbers only what they need.</summary>
    static void Widen(DataGridView grid, params string[] wide)
    {
        foreach (DataGridViewColumn c in grid.Columns)
            c.FillWeight = wide.Contains(c.Name) ? 300 : c.DefaultCellStyle.Alignment == DataGridViewContentAlignment.MiddleRight ? 60 : 100;
    }

    public void SetExpertMode(bool on)
    {
        expert = on;
        GridBinder.ApplyExpert<BundleRowViewModel>(gridBundles, on);
    }
}
