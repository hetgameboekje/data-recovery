namespace RecoveryUI.Views;

partial class ReportView
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
        layout = new TableLayoutPanel();
        flowTiles = new FlowLayoutPanel();
        tileTotal = new Controls.StatTile();
        tileUnique = new Controls.StatTile();
        tileExtra = new Controls.StatTile();
        tilePending = new Controls.StatTile();
        lblNeeded = new Label();
        panelPending = new FlowLayoutPanel();
        lblPending = new Label();
        btnHashPending = new Button();
        btnRefresh = new Button();
        btnCheckDuplicates = new Button();
        tabs = new TabControl();
        tabTypes = new TabPage();
        gridTypes = new DataGridView();
        tabDisks = new TabPage();
        gridDisks = new DataGridView();
        tabGroups = new TabPage();
        treeGroups = new TreeView();
        panelGroups = new FlowLayoutPanel();
        lblTop = new Label();
        numTop = new NumericUpDown();
        btnGroups = new Button();
        layout.SuspendLayout();
        flowTiles.SuspendLayout();
        panelPending.SuspendLayout();
        tabs.SuspendLayout();
        tabTypes.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)gridTypes).BeginInit();
        tabDisks.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)gridDisks).BeginInit();
        tabGroups.SuspendLayout();
        panelGroups.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)numTop).BeginInit();
        SuspendLayout();
        //
        // layout
        //
        layout.ColumnCount = 1;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(flowTiles, 0, 0);
        layout.Controls.Add(lblNeeded, 0, 1);
        layout.Controls.Add(panelPending, 0, 2);
        layout.Controls.Add(tabs, 0, 3);
        layout.Dock = DockStyle.Fill;
        layout.Name = "layout";
        layout.RowCount = 4;
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        //
        // flowTiles
        //
        flowTiles.AutoSize = true;
        flowTiles.Controls.Add(tileTotal);
        flowTiles.Controls.Add(tileUnique);
        flowTiles.Controls.Add(tileExtra);
        flowTiles.Controls.Add(tilePending);
        flowTiles.Margin = new Padding(0);
        flowTiles.Name = "flowTiles";
        //
        // tileTotal
        //
        tileTotal.Caption = "files";
        tileTotal.Name = "tileTotal";
        tileTotal.Title = "Everything on all disks";
        //
        // tileUnique
        //
        tileUnique.Caption = "one copy of every file";
        tileUnique.Name = "tileUnique";
        tileUnique.Title = "Unique data";
        //
        // tileExtra
        //
        tileExtra.Caption = "space taken by duplicates";
        tileExtra.Name = "tileExtra";
        tileExtra.Title = "Extra copies";
        //
        // tilePending
        //
        tilePending.Caption = "might still be duplicates";
        tilePending.Name = "tilePending";
        tilePending.Title = "Not yet decided";
        //
        // lblNeeded
        //
        lblNeeded.AutoSize = true;
        lblNeeded.Margin = new Padding(0, 4, 0, 4);
        lblNeeded.MaximumSize = new Size(900, 0);
        lblNeeded.Name = "lblNeeded";
        lblNeeded.Text = "…";
        //
        // panelPending
        //
        panelPending.AutoSize = true;
        panelPending.Controls.Add(lblPending);
        panelPending.Controls.Add(btnHashPending);
        panelPending.Controls.Add(btnCheckDuplicates);
        panelPending.Controls.Add(btnRefresh);
        panelPending.Margin = new Padding(0, 0, 0, 8);
        panelPending.Name = "panelPending";
        //
        // lblPending
        //
        lblPending.AutoSize = true;
        lblPending.Margin = new Padding(0, 8, 12, 0);
        lblPending.MaximumSize = new Size(560, 0);
        lblPending.Name = "lblPending";
        lblPending.Text = "";
        //
        // btnHashPending
        //
        btnHashPending.Name = "btnHashPending";
        btnHashPending.Text = "Fingerprint the rest";
        //
        // btnRefresh
        //
        btnRefresh.Margin = new Padding(12, 3, 3, 3);
        btnRefresh.Name = "btnRefresh";
        btnRefresh.Text = "Refresh";
        //
        // btnCheckDuplicates
        //
        btnCheckDuplicates.Margin = new Padding(12, 3, 3, 3);
        btnCheckDuplicates.Name = "btnCheckDuplicates";
        btnCheckDuplicates.Text = "Check duplicates";
        //
        // tabs
        //
        tabs.Controls.Add(tabTypes);
        tabs.Controls.Add(tabDisks);
        tabs.Controls.Add(tabGroups);
        tabs.Dock = DockStyle.Fill;
        tabs.Name = "tabs";
        //
        // tabTypes
        //
        tabTypes.Controls.Add(gridTypes);
        tabTypes.Name = "tabTypes";
        tabTypes.Padding = new Padding(6);
        tabTypes.Text = "By file type";
        //
        // gridTypes
        //
        gridTypes.Dock = DockStyle.Fill;
        gridTypes.Name = "gridTypes";
        //
        // tabDisks
        //
        tabDisks.Controls.Add(gridDisks);
        tabDisks.Name = "tabDisks";
        tabDisks.Padding = new Padding(6);
        tabDisks.Text = "Per disk";
        //
        // gridDisks
        //
        gridDisks.Dock = DockStyle.Fill;
        gridDisks.Name = "gridDisks";
        //
        // tabGroups
        //
        tabGroups.Controls.Add(treeGroups);
        tabGroups.Controls.Add(panelGroups);
        tabGroups.Name = "tabGroups";
        tabGroups.Padding = new Padding(6);
        tabGroups.Text = "Largest duplicate groups";
        //
        // treeGroups
        //
        treeGroups.BorderStyle = BorderStyle.None;
        treeGroups.Dock = DockStyle.Fill;
        treeGroups.Name = "treeGroups";
        //
        // panelGroups
        //
        panelGroups.AutoSize = true;
        panelGroups.Controls.Add(lblTop);
        panelGroups.Controls.Add(numTop);
        panelGroups.Controls.Add(btnGroups);
        panelGroups.Dock = DockStyle.Top;
        panelGroups.Name = "panelGroups";
        //
        // lblTop
        //
        lblTop.AutoSize = true;
        lblTop.Margin = new Padding(3, 8, 3, 0);
        lblTop.Name = "lblTop";
        lblTop.Text = "Show the largest";
        //
        // numTop
        //
        numTop.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
        numTop.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        numTop.Name = "numTop";
        numTop.Size = new Size(64, 25);
        numTop.Value = new decimal(new int[] { 25, 0, 0, 0 });
        //
        // btnGroups
        //
        btnGroups.Name = "btnGroups";
        btnGroups.Text = "Show groups";
        //
        // ReportView
        //
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(layout);
        Name = "ReportView";
        Size = new Size(940, 640);
        layout.ResumeLayout(false);
        layout.PerformLayout();
        flowTiles.ResumeLayout(false);
        panelPending.ResumeLayout(false);
        panelPending.PerformLayout();
        tabs.ResumeLayout(false);
        tabTypes.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)gridTypes).EndInit();
        tabDisks.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)gridDisks).EndInit();
        tabGroups.ResumeLayout(false);
        tabGroups.PerformLayout();
        panelGroups.ResumeLayout(false);
        panelGroups.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)numTop).EndInit();
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel layout;
    private FlowLayoutPanel flowTiles;
    private Controls.StatTile tileTotal;
    private Controls.StatTile tileUnique;
    private Controls.StatTile tileExtra;
    private Controls.StatTile tilePending;
    private Label lblNeeded;
    private FlowLayoutPanel panelPending;
    private Label lblPending;
    private Button btnHashPending;
    private Button btnRefresh;
    private Button btnCheckDuplicates;
    private TabControl tabs;
    private TabPage tabTypes;
    private DataGridView gridTypes;
    private TabPage tabDisks;
    private DataGridView gridDisks;
    private TabPage tabGroups;
    private TreeView treeGroups;
    private FlowLayoutPanel panelGroups;
    private Label lblTop;
    private NumericUpDown numTop;
    private Button btnGroups;
}
