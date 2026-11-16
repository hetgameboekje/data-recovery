namespace RecoveryUI.Views;

partial class JunkView
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
        lblIntro = new Label();
        panelTop = new FlowLayoutPanel();
        btnRefresh = new Button();
        btnRules = new Button();
        lblSummary = new Label();
        flowTiles = new FlowLayoutPanel();
        tileJunk = new Controls.StatTile();
        tileKept = new Controls.StatTile();
        tileReview = new Controls.StatTile();
        tabs = new TabControl();
        tabGroups = new TabPage();
        tabBundles = new TabPage();
        tabExamples = new TabPage();
        lblGroups = new Label();
        gridGroups = new DataGridView();
        lblBundles = new Label();
        gridBundles = new DataGridView();
        lblExamples = new Label();
        gridExamples = new DataGridView();
        menu = new ContextMenuStrip(components);
        menuOpen = new ToolStripMenuItem();
        menuCopyPath = new ToolStripMenuItem();
        layout.SuspendLayout();
        panelTop.SuspendLayout();
        flowTiles.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)gridGroups).BeginInit();
        ((System.ComponentModel.ISupportInitialize)gridBundles).BeginInit();
        ((System.ComponentModel.ISupportInitialize)gridExamples).BeginInit();
        menu.SuspendLayout();
        SuspendLayout();
        //
        // layout
        //
        layout.ColumnCount = 1;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(lblIntro, 0, 0);
        layout.Controls.Add(panelTop, 0, 1);
        layout.Controls.Add(flowTiles, 0, 2);
        layout.Controls.Add(tabs, 0, 3);
        layout.Dock = DockStyle.Fill;
        layout.Name = "layout";
        layout.Padding = new Padding(16, 8, 16, 8);
        layout.RowCount = 4;
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        //
        // tabs: one list at a time, each with the full height
        //
        tabs.Controls.Add(tabGroups);
        tabs.Controls.Add(tabBundles);
        tabs.Controls.Add(tabExamples);
        tabs.Dock = DockStyle.Fill;
        tabs.Margin = new Padding(3, 10, 3, 3);
        tabs.MinimumSize = new Size(0, 220);
        tabs.Name = "tabs";
        tabs.TabIndex = 2;
        tabGroups.Controls.Add(gridGroups);
        tabGroups.Controls.Add(lblGroups);
        tabGroups.Name = "tabGroups";
        tabGroups.Padding = new Padding(6);
        tabGroups.Text = "What was found";
        tabBundles.Controls.Add(gridBundles);
        tabBundles.Controls.Add(lblBundles);
        tabBundles.Name = "tabBundles";
        tabBundles.Padding = new Padding(6);
        tabBundles.Text = "Folders kept whole";
        tabExamples.Controls.Add(gridExamples);
        tabExamples.Controls.Add(lblExamples);
        tabExamples.Name = "tabExamples";
        tabExamples.Padding = new Padding(6);
        tabExamples.Text = "Largest junk files";
        layout.Size = new Size(900, 700);
        layout.TabIndex = 0;
        //
        // lblIntro
        //
        lblIntro.AutoSize = true;
        lblIntro.Margin = new Padding(3, 4, 3, 6);
        lblIntro.MaximumSize = new Size(1000, 0);
        lblIntro.Name = "lblIntro";
        lblIntro.Text =
            "Not everything on an old disk is worth a place between your photos. This page shows what the rules found in the catalog. " +
            "It moves and deletes NOTHING: it only decides where the Copy page puts these files. To move junk out of a folder, use the \"Clean up\" page.\n" +
            "•  Junk (thumbnails, icons, broken scraps, desktop.ini, caches) goes to _Review on the backup disk, after your own files and only if there is room.\n" +
            "•  Folders that only work complete (a Minecraft server, a database, a code project) are copied whole, in their own structure, " +
            "to \"Apps and data\". Programs you can reinstall are kept whole too, but in _Review.\n" +
            "The rules are in dictionaries.xml: change them, then press \"Check again\".";
        //
        // panelTop
        //
        panelTop.AutoSize = true;
        panelTop.Controls.Add(btnRefresh);
        panelTop.Controls.Add(btnRules);
        panelTop.Controls.Add(lblSummary);
        panelTop.Dock = DockStyle.Fill;
        panelTop.Name = "panelTop";
        //
        // btnRefresh
        //
        btnRefresh.Name = "btnRefresh";
        btnRefresh.TabIndex = 0;
        btnRefresh.Text = "Check again";
        //
        // btnRules
        //
        btnRules.Name = "btnRules";
        btnRules.TabIndex = 1;
        btnRules.Text = "Open the rules file";
        //
        // lblSummary
        //
        lblSummary.AutoSize = true;
        lblSummary.Margin = new Padding(12, 9, 3, 3);
        lblSummary.MaximumSize = new Size(700, 0);
        lblSummary.Name = "lblSummary";
        lblSummary.Text = "";
        //
        // flowTiles
        //
        flowTiles.AutoSize = true;
        flowTiles.Controls.Add(tileJunk);
        flowTiles.Controls.Add(tileKept);
        flowTiles.Controls.Add(tileReview);
        flowTiles.Name = "flowTiles";
        //
        // tileJunk
        //
        tileJunk.Caption = "files";
        tileJunk.Name = "tileJunk";
        tileJunk.Title = "Junk";
        //
        // tileKept
        //
        tileKept.Caption = "folders";
        tileKept.Name = "tileKept";
        tileKept.Title = "Kept whole";
        //
        // tileReview
        //
        tileReview.Caption = "folders";
        tileReview.Name = "tileReview";
        tileReview.Title = "Reinstallable programs";
        //
        // lblGroups
        //
        lblGroups.Dock = DockStyle.Top;
        lblGroups.Name = "lblGroups";
        lblGroups.Size = new Size(800, 28);
        lblGroups.Text = "Double-click a junk row to see its largest files.";
        //
        // gridGroups
        //
        gridGroups.Dock = DockStyle.Fill;
        gridGroups.MultiSelect = false;
        gridGroups.Name = "gridGroups";
        gridGroups.TabIndex = 2;
        //
        // lblBundles
        //
        lblBundles.Dock = DockStyle.Top;
        lblBundles.Name = "lblBundles";
        lblBundles.Size = new Size(800, 28);
        lblBundles.Text = "Never split by type, and no duplicates removed inside them. Right-click a folder to show it in Explorer.";
        //
        // gridBundles
        //
        gridBundles.ContextMenuStrip = menu;
        gridBundles.Dock = DockStyle.Fill;
        gridBundles.MultiSelect = false;
        gridBundles.Name = "gridBundles";
        gridBundles.TabIndex = 3;
        //
        // lblExamples
        //
        lblExamples.Dock = DockStyle.Top;
        lblExamples.Name = "lblExamples";
        lblExamples.Size = new Size(800, 28);
        lblExamples.Text = "Largest files";
        //
        // gridExamples
        //
        gridExamples.ContextMenuStrip = menu;
        gridExamples.Dock = DockStyle.Fill;
        gridExamples.MultiSelect = false;
        gridExamples.Name = "gridExamples";
        gridExamples.TabIndex = 4;
        //
        // menu
        //
        menu.Items.AddRange(new ToolStripItem[] { menuOpen, menuCopyPath });
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
        // JunkView
        //
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(layout);
        Font = new Font("Segoe UI", 9.5F);
        Name = "JunkView";
        Size = new Size(900, 700);
        layout.ResumeLayout(false);
        layout.PerformLayout();
        panelTop.ResumeLayout(false);
        panelTop.PerformLayout();
        flowTiles.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)gridGroups).EndInit();
        ((System.ComponentModel.ISupportInitialize)gridBundles).EndInit();
        ((System.ComponentModel.ISupportInitialize)gridExamples).EndInit();
        menu.ResumeLayout(false);
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel layout;
    private Label lblIntro;
    private FlowLayoutPanel panelTop;
    private Button btnRefresh;
    private Button btnRules;
    private Label lblSummary;
    private FlowLayoutPanel flowTiles;
    private Controls.StatTile tileJunk;
    private Controls.StatTile tileKept;
    private Controls.StatTile tileReview;
    private TabControl tabs;
    private TabPage tabGroups;
    private TabPage tabBundles;
    private TabPage tabExamples;
    private Label lblGroups;
    private DataGridView gridGroups;
    private Label lblBundles;
    private DataGridView gridBundles;
    private Label lblExamples;
    private DataGridView gridExamples;
    private ContextMenuStrip menu;
    private ToolStripMenuItem menuOpen;
    private ToolStripMenuItem menuCopyPath;
}
