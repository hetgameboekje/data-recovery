namespace RecoveryUI.Views;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null)) components.Dispose();
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        panelContent = new Panel();
        panelLog = new Panel();
        txtLog = new TextBox();
        lblLog = new Label();
        panelHeader = new Panel();
        chkExpert = new CheckBox();
        lblPageTitle = new Label();
        panelNav = new FlowLayoutPanel();
        lblApp = new Label();
        btnHome = new Button();
        btnDisks = new Button();
        btnScan = new Button();
        btnReport = new Button();
        btnSettings = new Button();
        lblComing = new Label();
        btnFutureCopy = new Button();
        btnFutureJunk = new Button();
        btnFutureCorrupt = new Button();
        btnFutureDeep = new Button();
        btnFutureNetwork = new Button();
        statusBar = new TableLayoutPanel();
        lblCatalog = new Label();
        lblStatus = new Label();
        progressBar = new ProgressBar();
        btnCancel = new Button();
        panelLog.SuspendLayout();
        panelHeader.SuspendLayout();
        panelNav.SuspendLayout();
        statusBar.SuspendLayout();
        SuspendLayout();
        //
        // panelContent
        //
        panelContent.Dock = DockStyle.Fill;
        panelContent.Name = "panelContent";
        panelContent.Padding = new Padding(24, 8, 24, 16);
        //
        // panelLog
        //
        panelLog.Controls.Add(txtLog);
        panelLog.Controls.Add(lblLog);
        panelLog.Dock = DockStyle.Bottom;
        panelLog.Name = "panelLog";
        panelLog.Padding = new Padding(24, 0, 24, 8);
        panelLog.Size = new Size(800, 120);
        panelLog.Visible = false;
        //
        // txtLog
        //
        txtLog.BorderStyle = BorderStyle.FixedSingle;
        txtLog.Dock = DockStyle.Fill;
        txtLog.Font = new Font("Consolas", 9F);
        txtLog.Multiline = true;
        txtLog.Name = "txtLog";
        txtLog.ReadOnly = true;
        txtLog.ScrollBars = ScrollBars.Vertical;
        //
        // lblLog
        //
        lblLog.Dock = DockStyle.Top;
        lblLog.Name = "lblLog";
        lblLog.Size = new Size(752, 22);
        lblLog.Text = "Live log (also written to the log folder, see Settings)";
        //
        // panelHeader
        //
        panelHeader.Controls.Add(chkExpert);
        panelHeader.Controls.Add(lblPageTitle);
        panelHeader.Dock = DockStyle.Top;
        panelHeader.Name = "panelHeader";
        panelHeader.Padding = new Padding(24, 12, 24, 4);
        panelHeader.Size = new Size(800, 60);
        //
        // chkExpert
        //
        chkExpert.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        chkExpert.Appearance = Appearance.Button;
        chkExpert.FlatStyle = FlatStyle.System;
        chkExpert.Location = new Point(620, 16);
        chkExpert.Name = "chkExpert";
        chkExpert.Size = new Size(156, 30);
        chkExpert.Text = "Expert mode: off";
        chkExpert.TextAlign = ContentAlignment.MiddleCenter;
        //
        // lblPageTitle
        //
        lblPageTitle.AutoSize = true;
        lblPageTitle.Location = new Point(22, 14);
        lblPageTitle.Name = "lblPageTitle";
        lblPageTitle.Text = "Home";
        //
        // panelNav
        //
        panelNav.Controls.Add(lblApp);
        panelNav.Controls.Add(btnHome);
        panelNav.Controls.Add(btnDisks);
        panelNav.Controls.Add(btnScan);
        panelNav.Controls.Add(btnReport);
        panelNav.Controls.Add(btnSettings);
        panelNav.Controls.Add(lblComing);
        panelNav.Controls.Add(btnFutureCopy);
        panelNav.Controls.Add(btnFutureJunk);
        panelNav.Controls.Add(btnFutureCorrupt);
        panelNav.Controls.Add(btnFutureDeep);
        panelNav.Controls.Add(btnFutureNetwork);
        panelNav.Dock = DockStyle.Left;
        panelNav.FlowDirection = FlowDirection.TopDown;
        panelNav.Name = "panelNav";
        panelNav.Padding = new Padding(8, 12, 8, 8);
        panelNav.Size = new Size(212, 640);
        panelNav.WrapContents = false;
        //
        // lblApp
        //
        lblApp.Margin = new Padding(8, 4, 3, 16);
        lblApp.Name = "lblApp";
        lblApp.Size = new Size(190, 28);
        lblApp.Text = "Recovery Toolkit";
        //
        // btnHome
        //
        btnHome.Name = "btnHome";
        btnHome.Size = new Size(194, 38);
        btnHome.Text = "   Home";
        //
        // btnDisks
        //
        btnDisks.Name = "btnDisks";
        btnDisks.Size = new Size(194, 38);
        btnDisks.Text = "   Disks";
        //
        // btnScan
        //
        btnScan.Name = "btnScan";
        btnScan.Size = new Size(194, 38);
        btnScan.Text = "   Catalog disks";
        //
        // btnReport
        //
        btnReport.Name = "btnReport";
        btnReport.Size = new Size(194, 38);
        btnReport.Text = "   Report";
        //
        // btnSettings
        //
        btnSettings.Name = "btnSettings";
        btnSettings.Size = new Size(194, 38);
        btnSettings.Text = "   Settings";
        //
        // lblComing
        //
        lblComing.Margin = new Padding(8, 24, 3, 6);
        lblComing.Name = "lblComing";
        lblComing.Size = new Size(190, 20);
        lblComing.Text = "COMING NEXT";
        //
        // btnFutureCopy
        //
        btnFutureCopy.Enabled = false;
        btnFutureCopy.Name = "btnFutureCopy";
        btnFutureCopy.Size = new Size(194, 34);
        btnFutureCopy.Tag = "Phase 2: plan and copy one copy of everything to the backup disks (newest version wins).";
        btnFutureCopy.Text = "   Copy plan";
        //
        // btnFutureJunk
        //
        btnFutureJunk.Enabled = false;
        btnFutureJunk.Name = "btnFutureJunk";
        btnFutureJunk.Size = new Size(194, 34);
        btnFutureJunk.Tag = "Phase 3/3b: game assets, icons, skins, thumbnails and low-quality previews.";
        btnFutureJunk.Text = "   Clean up junk";
        //
        // btnFutureCorrupt
        //
        btnFutureCorrupt.Enabled = false;
        btnFutureCorrupt.Name = "btnFutureCorrupt";
        btnFutureCorrupt.Size = new Size(194, 34);
        btnFutureCorrupt.Tag = "Phase 4: find corrupt files, move them aside and try to repair them.";
        btnFutureCorrupt.Text = "   Corrupt files";
        //
        // btnFutureDeep
        //
        btnFutureDeep.Enabled = false;
        btnFutureDeep.Name = "btnFutureDeep";
        btnFutureDeep.Size = new Size(194, 34);
        btnFutureDeep.Tag = "Phase 5: deep recovery (PhotoRec), undelete, disk health and imaging - ported from the PowerShell toolkit.";
        btnFutureDeep.Text = "   Deep recovery";
        //
        // btnFutureNetwork
        //
        btnFutureNetwork.Enabled = false;
        btnFutureNetwork.Name = "btnFutureNetwork";
        btnFutureNetwork.Size = new Size(194, 34);
        btnFutureNetwork.Tag = "Phase 9 (optional): upload recovered files to a network destination over SFTP/FTP.";
        btnFutureNetwork.Text = "   Network export";
        //
        // statusBar
        //
        statusBar.BackColor = Color.FromArgb(243, 244, 246);
        statusBar.ColumnCount = 4;
        statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
        statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
        statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200F));
        statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statusBar.Controls.Add(lblCatalog, 0, 0);
        statusBar.Controls.Add(lblStatus, 1, 0);
        statusBar.Controls.Add(progressBar, 2, 0);
        statusBar.Controls.Add(btnCancel, 3, 0);
        statusBar.Dock = DockStyle.Bottom;
        statusBar.Name = "statusBar";
        statusBar.Padding = new Padding(8, 2, 8, 2);
        statusBar.RowCount = 1;
        statusBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        statusBar.Size = new Size(1100, 36);
        //
        // lblCatalog
        //
        lblCatalog.AutoEllipsis = true;
        lblCatalog.Dock = DockStyle.Fill;
        lblCatalog.TextAlign = ContentAlignment.MiddleLeft;
        lblCatalog.Name = "lblCatalog";
        lblCatalog.Text = "Catalog: -";
        //
        // lblStatus
        //
        lblStatus.AutoEllipsis = true;
        lblStatus.Dock = DockStyle.Fill;
        lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        lblStatus.Name = "lblStatus";
        lblStatus.Text = "Ready";
        //
        // progressBar
        //
        progressBar.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        progressBar.Name = "progressBar";
        progressBar.Size = new Size(190, 16);
        progressBar.Visible = false;
        //
        // btnCancel
        //
        btnCancel.Anchor = AnchorStyles.Right;
        btnCancel.Name = "btnCancel";
        btnCancel.Text = "Stop";
        btnCancel.Visible = false;
        //
        // MainForm
        //
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1180, 760);
        Controls.Add(panelContent);
        Controls.Add(panelLog);
        Controls.Add(panelHeader);
        Controls.Add(panelNav);
        Controls.Add(statusBar);
        MinimumSize = new Size(980, 640);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Recovery Toolkit";
        panelLog.ResumeLayout(false);
        panelLog.PerformLayout();
        panelHeader.ResumeLayout(false);
        panelHeader.PerformLayout();
        panelNav.ResumeLayout(false);
        statusBar.ResumeLayout(false);
        statusBar.PerformLayout();
        ResumeLayout(false);
    }

    #endregion

    private Panel panelContent;
    private Panel panelLog;
    private TextBox txtLog;
    private Label lblLog;
    private Panel panelHeader;
    private CheckBox chkExpert;
    private Label lblPageTitle;
    private FlowLayoutPanel panelNav;
    private Label lblApp;
    private Button btnHome;
    private Button btnDisks;
    private Button btnScan;
    private Button btnReport;
    private Button btnSettings;
    private Label lblComing;
    private Button btnFutureCopy;
    private Button btnFutureJunk;
    private Button btnFutureCorrupt;
    private Button btnFutureDeep;
    private Button btnFutureNetwork;
    private TableLayoutPanel statusBar;
    private Label lblCatalog;
    private Label lblStatus;
    private ProgressBar progressBar;
    private Button btnCancel;
}
