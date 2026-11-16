using RecoveryBLL.Models;
using RecoveryUI.Infrastructure;
using RecoveryUI.ViewModels;

namespace RecoveryUI.Views;

/// <summary>
/// Clean up: duplicates and junk on one page (agreed with the user, docs/cleanup-session-2026-11-12.md).
///   1. the folder to clean, 2. where the unwanted files go, 3. optional: merge into master folders;
///   Find shows the list (nothing changes), Move asks once more, then moves. Undo puts the last run back.
///   Deleting is a separate menu: empty the clean-up folder (two confirmations).
/// </summary>
public sealed class CleanupView : UserControl, ICleanupView
{
    public event EventHandler? FindRequested;
    public event EventHandler? MoveRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? UndoRequested;
    public event EventHandler? EmptyTargetRequested;
    public event EventHandler? FilterChanged;
    public event EventHandler<string>? OpenInExplorerRequested;
    public Control Control => this;

    readonly TextBox txtSource = Path(@"e.g. G:\Videos, or G:\From_E to merge");
    readonly TextBox txtTarget = Path(@"e.g. G:\duplicates");
    readonly CheckBox chkMerge = new() { AutoSize = true, Text = "Merge: move the files I want into master folders at", Margin = new Padding(3, 7, 3, 3) };
    readonly TextBox txtMaster = Path(@"e.g. G:\  (files go to G:\Videos, G:\Photos, ...)");
    readonly FlowLayoutPanel panelCategories = new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 4, 0, 4) };
    readonly Dictionary<CleanupCategory, CheckBox> boxes = [];
    readonly Button btnFind = new() { Text = "Find" };
    readonly Button btnMove = new() { Text = "Move…" };
    readonly Button btnStop = new() { Text = "Stop" };
    readonly Button btnUndo = new() { Text = "Undo last clean up…" };
    readonly Button btnEmpty = new() { Text = "Empty the clean-up folder…" };
    readonly Label lblStatus = new() { AutoSize = true, Margin = new Padding(12, 9, 3, 3), MaximumSize = new Size(700, 0) };
    readonly Label lblDecisions = new() { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(3, 4, 3, 4), Visible = false };
    readonly ProgressBar progress = new() { Dock = DockStyle.Top, Height = 6, Visible = false, Style = ProgressBarStyle.Continuous };
    readonly DataGridView grid = new() { Dock = DockStyle.Fill };
    bool expert, running, canMove;

    public CleanupView()
    {
        Font = new Font("Segoe UI", 9.5f);
        BackColor = UiTheme.Surface;
        var intro = new Label
        {
            AutoSize = true, MaximumSize = new Size(1000, 0), ForeColor = UiTheme.Muted, Margin = new Padding(3, 4, 3, 8),
            Text = "Moves only the files you don't want out of a folder, into the clean-up folder, sorted by why and by type " +
                   "(e.g. duplicates\\Same video\\Videos). The version you want always stays. Find changes nothing; Move asks first. " +
                   "Nothing is deleted here, and Undo puts the last clean up back.",
        };

        var paths = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 3 };
        paths.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        paths.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        paths.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddPathRow(paths, 0, Caption("Folder to clean:"), txtSource, "Which folder holds the files to clean up?");
        AddPathRow(paths, 1, Caption("Unwanted files go to:"), txtTarget, "Where should the unwanted files go? (Not inside the folder being cleaned)");
        AddPathRow(paths, 2, chkMerge, txtMaster, "Where are the master folders (the folder that holds Videos, Photos, ...)?");

        foreach (var c in CleanupCategories.All)
        {
            var box = new CheckBox { AutoSize = true, Checked = true, Text = c.FolderName(), Margin = new Padding(3, 3, 14, 3) };
            new ToolTip().SetToolTip(box, c.Explanation());
            box.CheckedChanged += (_, _) => FilterChanged?.Invoke(this, EventArgs.Empty);
            boxes[c] = box;
            panelCategories.Controls.Add(box);
        }

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        buttons.Controls.AddRange([btnFind, btnMove, btnStop, btnUndo, btnEmpty, lblStatus]);
        UiTheme.Primary(btnFind);
        UiTheme.Primary(btnMove);
        UiTheme.Secondary(btnStop);
        UiTheme.Secondary(btnUndo);
        UiTheme.Secondary(btnEmpty);
        btnUndo.Margin = new Padding(24, 3, 3, 3);
        btnEmpty.ForeColor = UiTheme.Warning;
        new ToolTip().SetToolTip(btnEmpty, "Deletes everything in the clean-up folder for good. Shows the exact list first and asks twice.");
        lblDecisions.ForeColor = UiTheme.Warning;

        var top = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1, Padding = new Padding(16, 8, 16, 4) };
        top.Controls.Add(intro, 0, 0);
        top.Controls.Add(paths, 0, 1);
        top.Controls.Add(panelCategories, 0, 2);
        top.Controls.Add(buttons, 0, 3);
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 8) };
        body.Controls.Add(grid);
        body.Controls.Add(lblDecisions);
        body.Controls.Add(progress);
        Controls.Add(body);
        Controls.Add(top);

        UiTheme.Grid(grid);
        grid.MultiSelect = false;
        var menu = new ContextMenuStrip();
        var showFile = new ToolStripMenuItem("Show this file in Explorer");
        var showKeep = new ToolStripMenuItem("Show the copy that stays in Explorer");
        menu.Items.AddRange([showFile, showKeep]);
        grid.ContextMenuStrip = menu;
        showFile.Click += (_, _) => { if (GridBinder.Selected<CleanupRowViewModel>(grid) is { } r) OpenInExplorerRequested?.Invoke(this, r.Item.SourcePath); };
        showKeep.Click += (_, _) => { if (GridBinder.Selected<CleanupRowViewModel>(grid)?.Item.Keep is { } k) OpenInExplorerRequested?.Invoke(this, k); };
        grid.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0) { grid.ClearSelection(); grid.Rows[e.RowIndex].Selected = true; }
        };

        btnFind.Click += (_, _) => FindRequested?.Invoke(this, EventArgs.Empty);
        btnMove.Click += (_, _) => MoveRequested?.Invoke(this, EventArgs.Empty);
        btnStop.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
        btnUndo.Click += (_, _) => UndoRequested?.Invoke(this, EventArgs.Empty);
        btnEmpty.Click += (_, _) => EmptyTargetRequested?.Invoke(this, EventArgs.Empty);
        chkMerge.CheckedChanged += (_, _) => txtMaster.Enabled = chkMerge.Checked;
        txtSource.Leave += (_, _) => SuggestTarget();
        txtMaster.Enabled = false;
        SetUndo(null);
        Apply();
    }

    static TextBox Path(string hint) => new() { Dock = DockStyle.Fill, PlaceholderText = hint, Margin = new Padding(3, 4, 3, 3) };
    static Label Caption(string text) => new() { AutoSize = true, Text = text, Margin = new Padding(3, 8, 8, 3) };

    void AddPathRow(TableLayoutPanel table, int row, Control caption, TextBox box, string question)
    {
        var browse = new Button { Text = "Browse…" };
        UiTheme.Secondary(browse);
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = question, UseDescriptionForTitle = true, ShowNewFolderButton = true };
            if (Directory.Exists(box.Text)) dlg.InitialDirectory = box.Text;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            box.Text = dlg.SelectedPath;
            if (box == txtSource) SuggestTarget();
            if (box == txtMaster) chkMerge.Checked = true;
        };
        table.Controls.Add(caption, 0, row);
        table.Controls.Add(box, 1, row);
        table.Controls.Add(browse, 2, row);
    }

    /// <summary>"G:\Videos" -> "G:\duplicates", only when the person has not chosen one.</summary>
    void SuggestTarget()
    {
        if (txtTarget.Text.Length > 0 || txtSource.Text.Length < 2) return;
        var root = System.IO.Path.GetPathRoot(txtSource.Text);
        if (!string.IsNullOrEmpty(root)) txtTarget.Text = System.IO.Path.Combine(root, "duplicates");
    }

    public CleanupRequestViewModel Request => new(
        txtSource.Text.Trim(), txtTarget.Text.Trim(), chkMerge.Checked && txtMaster.Text.Trim().Length > 0 ? txtMaster.Text.Trim() : null,
        boxes.Where(kv => kv.Value.Checked).Select(kv => kv.Key).ToHashSet());

    public void ShowStatus(string text, bool warning = false) => this.OnUi(() =>
    {
        lblStatus.Text = text;
        lblStatus.ForeColor = warning ? UiTheme.Warning : UiTheme.Text;
    });

    public void ShowCounts(IReadOnlyList<CleanupCountViewModel> counts) => this.OnUi(() =>
    {
        foreach (var (c, box) in boxes)
        {
            var n = counts.FirstOrDefault(x => x.Category == c);
            box.Text = n == null ? c.FolderName() : n.Text;
        }
    });

    public void ShowRows(IReadOnlyList<CleanupRowViewModel> rows, string summary, bool move) => this.OnUi(() =>
    {
        GridBinder.Bind(grid, rows, expert);
        foreach (DataGridViewColumn c in grid.Columns)
            c.FillWeight = c.Name is "GoesTo" or "Stays" or "Why" ? 200 : c.Name == "Size" ? 60 : 110;
        canMove = move;
        ShowStatus(summary);
        Apply();
    });

    public void ShowDecisions(IReadOnlyList<string> lines) => this.OnUi(() =>
    {
        lblDecisions.Visible = lines.Count > 0;
        lblDecisions.Text = lines.Count == 0 ? "" :
            $"For you to decide ({lines.Count}); these files are not moved:\n" + string.Join("\n", lines.Take(8)) + (lines.Count > 8 ? "\n…" : "");
    });

    public void SetUndo(string? text) => this.OnUi(() =>
    {
        btnUndo.Enabled = text != null && !running;
        btnUndo.Tag = text;
        new ToolTip().SetToolTip(btnUndo, text ?? "Nothing to undo.");
    });

    public void SetRunning(bool on) => this.OnUi(() =>
    {
        running = on;
        progress.Visible = on;
        if (!on) progress.Value = 0;
        Apply();
    });

    void Apply()
    {
        btnFind.Enabled = !running;
        btnMove.Enabled = !running && canMove;
        btnStop.Visible = running;
        btnEmpty.Enabled = !running;
        btnUndo.Enabled = !running && btnUndo.Tag != null;
        foreach (var b in boxes.Values) b.Enabled = !running;
    }

    public void ShowProgress(ProgressViewModel p) => this.OnUi(() =>
    {
        progress.Style = p.Indeterminate ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        if (!p.Indeterminate) progress.Value = Math.Clamp(p.Percent, 0, 100);
        lblStatus.Text = $"{p.Stage}: {p.Detail}";
        lblStatus.ForeColor = UiTheme.Text;
    });

    public void SetExpertMode(bool on)
    {
        expert = on;
        GridBinder.ApplyExpert<CleanupRowViewModel>(grid, on);
    }
}
