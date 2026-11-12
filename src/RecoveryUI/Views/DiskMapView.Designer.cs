namespace RecoveryUI.Views;

partial class DiskMapView
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null)) components.Dispose();
        base.Dispose(disposing);
    }

    #region Component Designer generated code

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        layout = new TableLayoutPanel();
        panelTop = new FlowLayoutPanel();
        lblShow = new Label();
        cmbScope = new ComboBox();
        btnUp = new Button();
        btnRefresh = new Button();
        btnClearHighlight = new Button();
        btnSavePng = new Button();
        lblIntro = new Label();
        lblSummary = new Label();
        panelLegend = new FlowLayoutPanel();
        lblZoom = new Label();
        split = new SplitContainer();
        splitTop = new SplitContainer();
        tree = new TreeView();
        gridExtensions = new DataGridView();
        treemap = new Controls.TreemapControl();
        lblHover = new Label();
        menu = new ContextMenuStrip(components);
        menuOpen = new ToolStripMenuItem();
        menuCopyPath = new ToolStripMenuItem();
        menuZoomIn = new ToolStripMenuItem();
        menuZoomOut = new ToolStripMenuItem();
        layout.SuspendLayout();
        panelTop.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)split).BeginInit();
        split.Panel1.SuspendLayout();
        split.Panel2.SuspendLayout();
        split.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)splitTop).BeginInit();
        splitTop.Panel1.SuspendLayout();
        splitTop.Panel2.SuspendLayout();
        splitTop.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)gridExtensions).BeginInit();
        menu.SuspendLayout();
        SuspendLayout();
        //
        // layout
        //
        layout.ColumnCount = 1;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(panelTop, 0, 0);
        layout.Controls.Add(lblIntro, 0, 1);
        layout.Controls.Add(lblSummary, 0, 2);
        layout.Controls.Add(panelLegend, 0, 3);
        layout.Controls.Add(lblZoom, 0, 4);
        layout.Controls.Add(split, 0, 5);
        layout.Controls.Add(lblHover, 0, 6);
        layout.Dock = DockStyle.Fill;
        layout.Name = "layout";
        layout.Padding = new Padding(16, 8, 16, 8);
        layout.RowCount = 7;
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        layout.Size = new Size(900, 640);
        layout.TabIndex = 0;
        //
        // panelTop
        //
        panelTop.AutoSize = true;
        panelTop.Controls.Add(lblShow);
        panelTop.Controls.Add(cmbScope);
        panelTop.Controls.Add(btnUp);
        panelTop.Controls.Add(btnRefresh);
        panelTop.Controls.Add(btnClearHighlight);
        panelTop.Controls.Add(btnSavePng);
        panelTop.Dock = DockStyle.Fill;
        panelTop.Name = "panelTop";
        panelTop.WrapContents = true;
        //
        // lblShow
        //
        lblShow.AutoSize = true;
        lblShow.Margin = new Padding(3, 9, 3, 3);
        lblShow.Name = "lblShow";
        lblShow.Text = "Show:";
        //
        // cmbScope
        //
        cmbScope.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbScope.Margin = new Padding(3, 5, 12, 3);
        cmbScope.Name = "cmbScope";
        cmbScope.Size = new Size(380, 25);
        cmbScope.TabIndex = 0;
        //
        // btnUp
        //
        btnUp.Name = "btnUp";
        btnUp.TabIndex = 1;
        btnUp.Text = "Zoom out";
        //
        // btnRefresh
        //
        btnRefresh.Name = "btnRefresh";
        btnRefresh.TabIndex = 2;
        btnRefresh.Text = "Refresh";
        //
        // btnClearHighlight
        //
        btnClearHighlight.Name = "btnClearHighlight";
        btnClearHighlight.TabIndex = 3;
        btnClearHighlight.Text = "Show all types again";
        //
        // btnSavePng
        //
        btnSavePng.Name = "btnSavePng";
        btnSavePng.TabIndex = 4;
        btnSavePng.Text = "Save as PNG…";
        //
        // lblIntro
        //
        lblIntro.AutoSize = true;
        lblIntro.Margin = new Padding(3, 4, 3, 4);
        lblIntro.MaximumSize = new Size(1000, 0);
        lblIntro.Name = "lblIntro";
        lblIntro.Text =
            "What takes the space? Every block is a file: the bigger the block, the more space it takes. Colours are file types. " +
            "Striped blocks are extra copies of files that are also kept elsewhere. A dotted edge means \"not fingerprinted yet\". " +
            "Double-click to zoom into a folder. This page only looks at the catalog: nothing on your disks is changed, " +
            "and unplugged disks show too.";
        //
        // lblSummary
        //
        lblSummary.AutoSize = true;
        lblSummary.Margin = new Padding(3, 4, 3, 4);
        lblSummary.MaximumSize = new Size(1000, 0);
        lblSummary.Name = "lblSummary";
        lblSummary.Text = "";
        //
        // panelLegend
        //
        panelLegend.AutoSize = true;
        panelLegend.Dock = DockStyle.Fill;
        panelLegend.Name = "panelLegend";
        panelLegend.WrapContents = true;
        //
        // lblZoom
        //
        lblZoom.AutoEllipsis = true;
        lblZoom.Dock = DockStyle.Fill;
        lblZoom.Name = "lblZoom";
        lblZoom.Size = new Size(860, 22);
        lblZoom.Text = "";
        //
        // split
        //
        split.Dock = DockStyle.Fill;
        split.Name = "split";
        split.Orientation = Orientation.Horizontal;
        split.Panel1.Controls.Add(splitTop);
        split.Panel2.Controls.Add(treemap);
        split.Size = new Size(860, 420);
        split.SplitterDistance = 150;
        split.TabIndex = 5;
        //
        // splitTop
        //
        splitTop.Dock = DockStyle.Fill;
        splitTop.Name = "splitTop";
        splitTop.Panel1.Controls.Add(tree);
        splitTop.Panel2.Controls.Add(gridExtensions);
        splitTop.Size = new Size(860, 180);
        splitTop.SplitterDistance = 520;
        splitTop.TabIndex = 0;
        //
        // tree
        //
        tree.BorderStyle = BorderStyle.None;
        tree.Dock = DockStyle.Fill;
        tree.HideSelection = false;
        tree.Name = "tree";
        tree.TabIndex = 0;
        //
        // gridExtensions
        //
        gridExtensions.Dock = DockStyle.Fill;
        gridExtensions.MultiSelect = false;
        gridExtensions.Name = "gridExtensions";
        gridExtensions.TabIndex = 1;
        //
        // treemap
        //
        treemap.BackColor = Color.FromArgb(248, 249, 251);
        treemap.ContextMenuStrip = menu;
        treemap.Dock = DockStyle.Fill;
        treemap.Name = "treemap";
        treemap.TabIndex = 0;
        //
        // lblHover
        //
        lblHover.AutoEllipsis = true;
        lblHover.Dock = DockStyle.Fill;
        lblHover.Name = "lblHover";
        lblHover.Text = "Point at a block to see what it is.";
        lblHover.TextAlign = ContentAlignment.MiddleLeft;
        //
        // menu
        //
        menu.Items.AddRange(new ToolStripItem[] { menuOpen, menuCopyPath, menuZoomIn, menuZoomOut });
        menu.Name = "menu";
        //
        // menuOpen
        //
        menuOpen.Name = "menuOpen";
        menuOpen.Text = "Show in Explorer";
        //
        // menuCopyPath
        //
        menuCopyPath.Name = "menuCopyPath";
        menuCopyPath.Text = "Copy path";
        //
        // menuZoomIn
        //
        menuZoomIn.Name = "menuZoomIn";
        menuZoomIn.Text = "Zoom into this folder";
        //
        // menuZoomOut
        //
        menuZoomOut.Name = "menuZoomOut";
        menuZoomOut.Text = "Zoom out";
        //
        // DiskMapView
        //
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(layout);
        Font = new Font("Segoe UI", 9.5F);
        Name = "DiskMapView";
        Size = new Size(900, 640);
        layout.ResumeLayout(false);
        layout.PerformLayout();
        panelTop.ResumeLayout(false);
        panelTop.PerformLayout();
        split.Panel1.ResumeLayout(false);
        split.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)split).EndInit();
        split.ResumeLayout(false);
        splitTop.Panel1.ResumeLayout(false);
        splitTop.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitTop).EndInit();
        splitTop.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)gridExtensions).EndInit();
        menu.ResumeLayout(false);
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel layout;
    private FlowLayoutPanel panelTop;
    private Label lblShow;
    private ComboBox cmbScope;
    private Button btnUp;
    private Button btnRefresh;
    private Button btnClearHighlight;
    private Button btnSavePng;
    private Label lblIntro;
    private Label lblSummary;
    private FlowLayoutPanel panelLegend;
    private Label lblZoom;
    private SplitContainer split;
    private SplitContainer splitTop;
    private TreeView tree;
    private DataGridView gridExtensions;
    private Controls.TreemapControl treemap;
    private Label lblHover;
    private ContextMenuStrip menu;
    private ToolStripMenuItem menuOpen;
    private ToolStripMenuItem menuCopyPath;
    private ToolStripMenuItem menuZoomIn;
    private ToolStripMenuItem menuZoomOut;
}
