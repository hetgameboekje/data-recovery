using RecoveryBLL;
using RecoveryBLL.Dtos;
using RecoveryBLL.Models;
using RecoveryUI.Infrastructure;
using RecoveryUI.ViewModels;

namespace RecoveryUI.Views;

/// <summary>
/// Phase 2b: "what takes the space?" Simple mode shows the map, the legend and one summary; Expert mode adds the folder tree,
/// the extension list (click one to highlight its files) and "Save as PNG". Clicking a block selects it in the tree and back.
/// </summary>
public partial class DiskMapView : UserControl, IDiskMapView
{
    public event EventHandler? RefreshRequested;
    public event EventHandler<long?>? ScopeChanged;
    public event EventHandler<MapNode>? OpenInExplorerRequested;
    public Control Control => this;

    const int MaxTreeChildren = 300;

    DiskMapViewModel? vm;
    bool suppressScope, syncing, expert;

    public DiskMapView()
    {
        InitializeComponent();
        lblIntro.ForeColor = lblHover.ForeColor = UiTheme.Muted;
        lblSummary.Font = UiTheme.HeadingFont;
        lblZoom.Font = new Font("Segoe UI Semibold", 9.5f);
        foreach (var b in new[] { btnUp, btnRefresh, btnClearHighlight, btnSavePng }) UiTheme.Secondary(b);
        UiTheme.Grid(gridExtensions);

        cmbScope.SelectedIndexChanged += (_, _) =>
        {
            if (!suppressScope && cmbScope.SelectedItem is MapScopeViewModel s) ScopeChanged?.Invoke(this, s.DiskId);
        };
        btnRefresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        btnUp.Click += (_, _) => ZoomOut();
        btnClearHighlight.Click += (_, _) => Highlight(null);
        btnSavePng.Click += (_, _) => SavePng();

        treemap.HoverChanged += (_, n) => lblHover.Text = n == null ? "Point at a block to see what it is." : Describe(n);
        treemap.NodeClicked += (_, n) => SelectInTree(n);
        treemap.NodeDoubleClicked += (_, n) => ZoomTo(n);
        menu.Opening += (_, e) =>
        {
            var n = treemap.Selected;
            if (n == null) { e.Cancel = true; return; }
            menuZoomIn.Enabled = (n.IsContainer ? n : n.Parent) != treemap.Root;
            menuZoomOut.Enabled = treemap.Root?.Parent != null;
        };
        menuOpen.Click += (_, _) => { if (treemap.Selected is { } n) OpenInExplorerRequested?.Invoke(this, n); };
        menuCopyPath.Click += (_, _) => { if (treemap.Selected is { } n && vm != null) Clipboard.SetText(vm.PathOf(n)); };
        menuZoomIn.Click += (_, _) => { if (treemap.Selected is { } n) ZoomTo(n); };
        menuZoomOut.Click += (_, _) => ZoomOut();

        tree.BeforeExpand += (_, e) => Fill(e.Node!);
        tree.AfterSelect += (_, e) =>
        {
            if (syncing || e.Node?.Tag is not MapNode n) return;
            // A block outside the zoomed part: zoom out far enough to show it.
            if (treemap.Root != null && !n.Ancestry().Contains(treemap.Root)) ZoomTo(vm!.Root);
            treemap.Selected = n;
            lblHover.Text = Describe(n);
        };
        gridExtensions.CellClick += (_, _) =>
        {
            if (GridBinder.Selected<ExtensionRowViewModel>(gridExtensions) is { } row) Highlight(row.Ext);
        };

        // Tree and extension list share the top half; keep the list wide enough for its numbers.
        splitTop.SizeChanged += (_, _) =>
        {
            if (splitTop.Width > 200) splitTop.SplitterDistance = Math.Max(150, (int)(splitTop.Width * 0.42));
        };

        ShowLoading("");
        SetExpertMode(false);
    }

    public void ShowScopes(IReadOnlyList<MapScopeViewModel> scopes, long? selected) => this.OnUi(() =>
    {
        suppressScope = true;
        cmbScope.Items.Clear();
        foreach (var s in scopes) cmbScope.Items.Add(s);
        cmbScope.SelectedItem = scopes.FirstOrDefault(s => s.DiskId == selected) ?? scopes.FirstOrDefault();
        suppressScope = false;
    });

    public void ShowLoading(string text) => this.OnUi(() =>
    {
        lblSummary.Text = text;
        lblSummary.ForeColor = UiTheme.Text;
        btnRefresh.Enabled = text.Length == 0;
    });

    public void ShowError(string text) => this.OnUi(() =>
    {
        lblSummary.Text = text;
        lblSummary.ForeColor = UiTheme.Warning;
        btnRefresh.Enabled = true;
    });

    public void ShowMap(DiskMapViewModel map) => this.OnUi(() =>
    {
        vm = map;
        btnRefresh.Enabled = true;
        lblSummary.Text = map.Summary;
        lblSummary.ForeColor = UiTheme.Text;

        panelLegend.SuspendLayout();
        panelLegend.Controls.Clear();
        foreach (var l in map.Legend)
            panelLegend.Controls.Add(LegendItem(l.Color, $"{l.Name}  {l.Size}", hatched: false, dotted: false));
        panelLegend.Controls.Add(LegendItem(Color.FromArgb(150, 150, 150), "striped = extra copy", hatched: true, dotted: false));
        panelLegend.Controls.Add(LegendItem(Color.FromArgb(150, 150, 150), "dotted edge = not fingerprinted yet", hatched: false, dotted: true));
        panelLegend.ResumeLayout();

        GridBinder.Bind(gridExtensions, map.Extensions, expert);

        tree.BeginUpdate();
        tree.Nodes.Clear();
        var top = TreeNodeFor(map.Root);
        tree.Nodes.Add(top);
        Fill(top);
        top.Expand();
        tree.EndUpdate();

        Highlight(null);
        ZoomTo(map.Root);
    });

    public void SetExpertMode(bool on)
    {
        expert = on;
        split.Panel1Collapsed = !on;
        btnSavePng.Visible = on;
        GridBinder.ApplyExpert<ExtensionRowViewModel>(gridExtensions, on);
    }

    // ---- zoom, highlight, selection

    void ZoomTo(MapNode n)
    {
        if (vm == null) return;
        var target = n.IsContainer ? n : n.Parent ?? n;
        var selected = treemap.Selected;
        treemap.SetMap(target, vm.Colors);
        if (selected != null && selected.Ancestry().Contains(target)) treemap.Selected = selected;
        btnUp.Enabled = target.Parent != null;
        lblZoom.Text = $"Showing: {vm.PathOf(target)}   ({Format.Bytes(target.Bytes)}, {target.Files:N0} files)";
    }

    void ZoomOut()
    {
        if (treemap.Root?.Parent is { } up) ZoomTo(up);
    }

    void Highlight(string? ext)
    {
        treemap.HighlightExtension = ext;
        btnClearHighlight.Visible = ext != null;
        if (ext == null) gridExtensions.ClearSelection();
    }

    string Describe(MapNode n)
    {
        var path = vm?.PathOf(n) ?? n.Name;
        var when = n.ModifiedTicks > 0 ? new DateTime(n.ModifiedTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd") : "";
        string what = n.Kind switch
        {
            MapNodeKind.File => n.State switch
            {
                MapFileState.ExtraCopy => "extra copy: the same file is also kept elsewhere",
                MapFileState.Kept => "has copies elsewhere; this one counts as the original",
                MapFileState.Pending => "not fingerprinted yet",
                _ => "the only copy",
            },
            MapNodeKind.SmallFiles => $"{n.Name}, merged into one block",
            _ => $"{n.Files:N0} files" + (n.ExtraCopyBytes > 0 ? $", {Format.Bytes(n.ExtraCopyBytes)} extra copies" : ""),
        };
        var type = vm != null && n.Category < vm.CategoryNames.Count ? vm.CategoryNames[n.Category] : "";
        return n.Kind == MapNodeKind.SmallFiles
            ? $"{path}   ·   {Format.Bytes(n.Bytes)}   ·   {what}"
            : $"{path}   ·   {Format.Bytes(n.Bytes)}   ·   {type}   ·   {what}" + (when.Length > 0 ? $"   ·   changed {when}" : "");
    }

    // ---- folder tree (filled when expanded, so a million files never become a million tree nodes)

    static TreeNode TreeNodeFor(MapNode n)
    {
        var share = n.Parent is { Bytes: > 0 } p ? $"  ({100.0 * n.Bytes / p.Bytes:0.#}%)" : "";
        var t = new TreeNode($"{n.Name}   {Format.Bytes(n.Bytes)}{share}") { Tag = n };
        if (n.Children.Count > 0) t.Nodes.Add(new TreeNode("…"));   // placeholder: real children on expand
        return t;
    }

    static void Fill(TreeNode t)
    {
        if (t.Nodes.Count != 1 || t.Nodes[0].Tag != null || t.Tag is not MapNode n) return;
        t.TreeView?.BeginUpdate();
        t.Nodes.Clear();
        foreach (var c in n.Children.Take(MaxTreeChildren)) t.Nodes.Add(TreeNodeFor(c));
        if (n.Children.Count > MaxTreeChildren)
            t.Nodes.Add(new TreeNode($"… {n.Children.Count - MaxTreeChildren:N0} smaller items (see the map)") { ForeColor = UiTheme.Muted });
        t.TreeView?.EndUpdate();
    }

    void SelectInTree(MapNode n)
    {
        lblHover.Text = Describe(n);
        if (!expert || tree.Nodes.Count == 0) return;
        syncing = true;
        try
        {
            var current = tree.Nodes[0];
            foreach (var step in n.Ancestry().Skip(1))
            {
                Fill(current);
                var next = current.Nodes.Cast<TreeNode>().FirstOrDefault(t => t.Tag == step);
                if (next == null) break;   // beyond the first MaxTreeChildren: stop at the folder
                current = next;
            }
            tree.SelectedNode = current;
            current.EnsureVisible();
        }
        finally { syncing = false; }
    }

    void SavePng()
    {
        using var bmp = treemap.Snapshot();
        if (bmp == null) return;
        using var dlg = new SaveFileDialog { Title = "Save the disk map", Filter = "PNG image|*.png", FileName = $"disk-map-{DateTime.Now:yyyyMMdd-HHmm}.png" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { bmp.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Png); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(this, ex.Message, "Save as PNG", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>A colour chip and its name: the name is always written out, so colour is never the only cue.</summary>
    static Control LegendItem(Color color, string text, bool hatched, bool dotted)
    {
        var chip = new Panel { Size = new Size(16, 16), Margin = new Padding(3, 5, 4, 3), BackColor = color };
        chip.Paint += (_, e) =>
        {
            if (hatched)
                using (var pen = new Pen(Color.FromArgb(70, 70, 70), 2))
                    for (int i = -16; i < 16; i += 6) e.Graphics.DrawLine(pen, i, 16, i + 16, 0);
            if (dotted)
                using (var pen = new Pen(Color.White, 2) { DashPattern = [1.5f, 1.5f] })
                    e.Graphics.DrawRectangle(pen, 1, 1, 13, 13);
        };
        var label = new Label { AutoSize = true, Text = text, Margin = new Padding(0, 4, 14, 3) };
        var item = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        item.Controls.Add(chip);
        item.Controls.Add(label);
        return item;
    }
}
