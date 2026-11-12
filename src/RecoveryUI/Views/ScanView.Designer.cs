namespace RecoveryUI.Views;

partial class ScanView
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
        scroll = new Panel();
        layout = new TableLayoutPanel();
        lblStep1 = new Label();
        lstTargets = new CheckedListBox();
        panelTargetButtons = new FlowLayoutPanel();
        btnAddFolder = new Button();
        btnRefresh = new Button();
        groupOptions = new GroupBox();
        tableOptions = new TableLayoutPanel();
        lblReaders = new Label();
        cmbReaders = new ComboBox();
        chkSha256 = new CheckBox();
        chkImageInfo = new CheckBox();
        chkListOnly = new CheckBox();
        lblExcludes = new Label();
        txtExcludes = new TextBox();
        lblStep2 = new Label();
        lblSafe = new Label();
        panelStart = new FlowLayoutPanel();
        btnStart = new Button();
        btnStop = new Button();
        panelProgress = new TableLayoutPanel();
        lblStage = new Label();
        progress = new ProgressBar();
        lblDetail = new Label();
        lblResult = new Label();
        layout.SuspendLayout();
        panelTargetButtons.SuspendLayout();
        groupOptions.SuspendLayout();
        tableOptions.SuspendLayout();
        panelStart.SuspendLayout();
        panelProgress.SuspendLayout();
        SuspendLayout();
        //
        // layout
        //
        layout.AutoSize = true;
        layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        layout.ColumnCount = 1;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(lblStep1, 0, 0);
        layout.Controls.Add(lstTargets, 0, 1);
        layout.Controls.Add(panelTargetButtons, 0, 2);
        layout.Controls.Add(groupOptions, 0, 3);
        layout.Controls.Add(lblStep2, 0, 4);
        layout.Controls.Add(lblSafe, 0, 5);
        layout.Controls.Add(panelStart, 0, 6);
        layout.Controls.Add(panelProgress, 0, 7);
        layout.Dock = DockStyle.Top;
        layout.Name = "layout";
        layout.RowCount = 8;
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        //
        // lblStep1
        //
        lblStep1.AutoSize = true;
        lblStep1.Margin = new Padding(0, 0, 0, 6);
        lblStep1.Name = "lblStep1";
        lblStep1.Text = "1.  Tick the drives or folders to catalog";
        //
        // lstTargets
        //
        lstTargets.CheckOnClick = true;
        lstTargets.Dock = DockStyle.Top;
        lstTargets.IntegralHeight = false;
        lstTargets.Name = "lstTargets";
        lstTargets.Size = new Size(900, 150);
        //
        // panelTargetButtons
        //
        panelTargetButtons.AutoSize = true;
        panelTargetButtons.Controls.Add(btnAddFolder);
        panelTargetButtons.Controls.Add(btnRefresh);
        panelTargetButtons.Margin = new Padding(0, 4, 0, 8);
        panelTargetButtons.Name = "panelTargetButtons";
        //
        // btnAddFolder
        //
        btnAddFolder.Name = "btnAddFolder";
        btnAddFolder.Text = "Add a folder…";
        //
        // btnRefresh
        //
        btnRefresh.Name = "btnRefresh";
        btnRefresh.Text = "Refresh drives";
        //
        // groupOptions
        //
        groupOptions.AutoSize = true;
        groupOptions.Controls.Add(tableOptions);
        groupOptions.Dock = DockStyle.Top;
        groupOptions.Name = "groupOptions";
        groupOptions.Padding = new Padding(10, 6, 10, 10);
        groupOptions.Text = "Expert options";
        //
        // tableOptions
        //
        tableOptions.AutoSize = true;
        tableOptions.ColumnCount = 2;
        tableOptions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tableOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tableOptions.Controls.Add(lblReaders, 0, 0);
        tableOptions.Controls.Add(cmbReaders, 1, 0);
        tableOptions.Controls.Add(chkSha256, 1, 1);
        tableOptions.Controls.Add(chkImageInfo, 1, 2);
        tableOptions.Controls.Add(chkListOnly, 1, 3);
        tableOptions.Controls.Add(lblExcludes, 0, 4);
        tableOptions.Controls.Add(txtExcludes, 1, 4);
        tableOptions.Dock = DockStyle.Top;
        tableOptions.Name = "tableOptions";
        tableOptions.RowCount = 5;
        tableOptions.RowStyles.Add(new RowStyle());
        tableOptions.RowStyles.Add(new RowStyle());
        tableOptions.RowStyles.Add(new RowStyle());
        tableOptions.RowStyles.Add(new RowStyle());
        tableOptions.RowStyles.Add(new RowStyle());
        //
        // lblReaders
        //
        lblReaders.Anchor = AnchorStyles.Left;
        lblReaders.AutoSize = true;
        lblReaders.Name = "lblReaders";
        lblReaders.Text = "Readers per disk";
        //
        // cmbReaders
        //
        cmbReaders.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbReaders.Name = "cmbReaders";
        cmbReaders.Size = new Size(220, 25);
        //
        // chkSha256
        //
        chkSha256.AutoSize = true;
        chkSha256.Name = "chkSha256";
        chkSha256.Text = "Also store SHA-256 (slower; xxHash128 is always stored)";
        //
        // chkImageInfo
        //
        chkImageInfo.AutoSize = true;
        chkImageInfo.Checked = true;
        chkImageInfo.CheckState = CheckState.Checked;
        chkImageInfo.Name = "chkImageInfo";
        chkImageInfo.Text = "Read image size and camera/date (needed later for junk and preview detection)";
        //
        // chkListOnly
        //
        chkListOnly.AutoSize = true;
        chkListOnly.Name = "chkListOnly";
        chkListOnly.Text = "Only list files now, fingerprint later";
        //
        // lblExcludes
        //
        lblExcludes.Anchor = AnchorStyles.Left;
        lblExcludes.AutoSize = true;
        lblExcludes.Name = "lblExcludes";
        lblExcludes.Text = "Skip folders (; separated)";
        //
        // txtExcludes
        //
        txtExcludes.Dock = DockStyle.Fill;
        txtExcludes.Name = "txtExcludes";
        txtExcludes.PlaceholderText = "e.g.  node_modules; D:\\Games";
        //
        // lblStep2
        //
        lblStep2.AutoSize = true;
        lblStep2.Margin = new Padding(0, 12, 0, 2);
        lblStep2.Name = "lblStep2";
        lblStep2.Text = "2.  Start";
        //
        // lblSafe
        //
        lblSafe.AutoSize = true;
        lblSafe.Margin = new Padding(0, 0, 0, 8);
        lblSafe.MaximumSize = new Size(900, 0);
        lblSafe.Name = "lblSafe";
        lblSafe.Text = "✓ Read-only: nothing on these disks is changed. You can stop at any time and continue later where it stopped.";
        //
        // panelStart
        //
        panelStart.AutoSize = true;
        panelStart.Controls.Add(btnStart);
        panelStart.Controls.Add(btnStop);
        panelStart.Margin = new Padding(0, 0, 0, 12);
        panelStart.Name = "panelStart";
        //
        // btnStart
        //
        btnStart.Name = "btnStart";
        btnStart.Text = "Start cataloguing";
        //
        // btnStop
        //
        btnStop.Margin = new Padding(12, 3, 3, 3);
        btnStop.Name = "btnStop";
        btnStop.Text = "Stop";
        //
        // panelProgress
        //
        panelProgress.AutoSize = true;
        panelProgress.ColumnCount = 1;
        panelProgress.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panelProgress.Controls.Add(lblStage, 0, 0);
        panelProgress.Controls.Add(progress, 0, 1);
        panelProgress.Controls.Add(lblDetail, 0, 2);
        panelProgress.Controls.Add(lblResult, 0, 3);
        panelProgress.Dock = DockStyle.Top;
        panelProgress.Name = "panelProgress";
        panelProgress.RowCount = 4;
        panelProgress.RowStyles.Add(new RowStyle());
        panelProgress.RowStyles.Add(new RowStyle());
        panelProgress.RowStyles.Add(new RowStyle());
        panelProgress.RowStyles.Add(new RowStyle());
        //
        // lblStage
        //
        lblStage.AutoSize = true;
        lblStage.Name = "lblStage";
        lblStage.Text = "Stage";
        //
        // progress
        //
        progress.Dock = DockStyle.Top;
        progress.Name = "progress";
        progress.Size = new Size(900, 18);
        //
        // lblDetail
        //
        lblDetail.AutoSize = true;
        lblDetail.Name = "lblDetail";
        lblDetail.Text = "";
        //
        // lblResult
        //
        lblResult.AutoSize = true;
        lblResult.Margin = new Padding(3, 8, 3, 0);
        lblResult.MaximumSize = new Size(900, 0);
        lblResult.Name = "lblResult";
        lblResult.Text = "";
        //
        // ScanView
        //
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        scroll.AutoScroll = true;
        scroll.Controls.Add(layout);
        scroll.Dock = DockStyle.Fill;
        scroll.Name = "scroll";
        Controls.Add(scroll);
        Name = "ScanView";
        Size = new Size(940, 640);
        layout.ResumeLayout(false);
        layout.PerformLayout();
        panelTargetButtons.ResumeLayout(false);
        groupOptions.ResumeLayout(false);
        groupOptions.PerformLayout();
        tableOptions.ResumeLayout(false);
        tableOptions.PerformLayout();
        panelStart.ResumeLayout(false);
        panelProgress.ResumeLayout(false);
        panelProgress.PerformLayout();
        ResumeLayout(false);
    }

    #endregion

    private Panel scroll;
    private TableLayoutPanel layout;
    private Label lblStep1;
    private CheckedListBox lstTargets;
    private FlowLayoutPanel panelTargetButtons;
    private Button btnAddFolder;
    private Button btnRefresh;
    private GroupBox groupOptions;
    private TableLayoutPanel tableOptions;
    private Label lblReaders;
    private ComboBox cmbReaders;
    private CheckBox chkSha256;
    private CheckBox chkImageInfo;
    private CheckBox chkListOnly;
    private Label lblExcludes;
    private TextBox txtExcludes;
    private Label lblStep2;
    private Label lblSafe;
    private FlowLayoutPanel panelStart;
    private Button btnStart;
    private Button btnStop;
    private TableLayoutPanel panelProgress;
    private Label lblStage;
    private ProgressBar progress;
    private Label lblDetail;
    private Label lblResult;
}
