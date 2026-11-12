namespace RecoveryUI.Views;

partial class CopyView
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
        panelIntro = new FlowLayoutPanel();
        lblIntroTitle = new Label();
        lblIntro = new Label();
        lblIntroExample = new Label();
        lblIntroDeep = new Label();
        lblStep2Hint = new Label();
        lblStep1 = new Label();
        lblStep1Hint = new Label();
        panelDest = new FlowLayoutPanel();
        lstDestinations = new ListBox();
        panelDestButtons = new FlowLayoutPanel();
        cmbDrives = new ComboBox();
        btnAddDrive = new Button();
        btnAddFolder = new Button();
        btnUp = new Button();
        btnDown = new Button();
        btnRemove = new Button();
        btnRefreshDrives = new Button();
        groupOptions = new GroupBox();
        flowOptions = new FlowLayoutPanel();
        chkDuplicates = new CheckBox();
        chkVerify = new CheckBox();
        chkWait = new CheckBox();
        chkAllowPending = new CheckBox();
        lblReserve = new Label();
        numReserve = new NumericUpDown();
        lblCap = new Label();
        numCap = new NumericUpDown();
        lblStep2 = new Label();
        panelPlanButtons = new FlowLayoutPanel();
        btnPlan = new Button();
        btnExport = new Button();
        panelPlan = new FlowLayoutPanel();
        flowTiles = new FlowLayoutPanel();
        tileToCopy = new Controls.StatTile();
        tileUnique = new Controls.StatTile();
        tileDuplicates = new Controls.StatTile();
        tileAlready = new Controls.StatTile();
        lblVerdict = new Label();
        gridDestinations = new DataGridView();
        lblNotes = new Label();
        panelDeletions = new FlowLayoutPanel();
        lblDeletions = new Label();
        btnReviewDeletions = new Button();
        lblStep3 = new Label();
        lblSafe = new Label();
        panelStart = new FlowLayoutPanel();
        btnStart = new Button();
        btnStop = new Button();
        panelProgress = new FlowLayoutPanel();
        lblStage = new Label();
        progress = new ProgressBar();
        lblDetail = new Label();
        lblResult = new Label();
        scroll.SuspendLayout();
        layout.SuspendLayout();
        panelIntro.SuspendLayout();
        panelDest.SuspendLayout();
        panelDestButtons.SuspendLayout();
        groupOptions.SuspendLayout();
        flowOptions.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)numReserve).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numCap).BeginInit();
        panelPlanButtons.SuspendLayout();
        panelPlan.SuspendLayout();
        flowTiles.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)gridDestinations).BeginInit();
        panelDeletions.SuspendLayout();
        panelStart.SuspendLayout();
        panelProgress.SuspendLayout();
        SuspendLayout();
        //
        // scroll
        //
        scroll.AutoScroll = true;
        scroll.Controls.Add(layout);
        scroll.Dock = DockStyle.Fill;
        scroll.Name = "scroll";
        scroll.Size = new Size(900, 900);
        //
        // layout
        //
        layout.AutoSize = true;
        layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        layout.ColumnCount = 1;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.Controls.Add(panelIntro, 0, 0);
        layout.Controls.Add(lblStep1, 0, 1);
        layout.Controls.Add(lblStep1Hint, 0, 2);
        layout.Controls.Add(panelDest, 0, 3);
        layout.Controls.Add(groupOptions, 0, 4);
        layout.Controls.Add(lblStep2, 0, 5);
        layout.Controls.Add(lblStep2Hint, 0, 6);
        layout.Controls.Add(panelPlanButtons, 0, 7);
        layout.Controls.Add(panelPlan, 0, 8);
        layout.Controls.Add(lblStep3, 0, 9);
        layout.Controls.Add(lblSafe, 0, 10);
        layout.Controls.Add(panelStart, 0, 11);
        layout.Controls.Add(panelProgress, 0, 12);
        layout.Location = new Point(0, 0);
        layout.Name = "layout";
        layout.Padding = new Padding(16, 8, 16, 24);
        layout.RowCount = 13;
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.TabIndex = 0;
        //
        // panelIntro
        //
        panelIntro.AutoSize = true;
        panelIntro.Controls.Add(lblIntroTitle);
        panelIntro.Controls.Add(lblIntro);
        panelIntro.Controls.Add(lblIntroExample);
        panelIntro.Controls.Add(lblIntroDeep);
        panelIntro.FlowDirection = FlowDirection.TopDown;
        panelIntro.Margin = new Padding(3, 3, 3, 8);
        panelIntro.Name = "panelIntro";
        panelIntro.Padding = new Padding(12, 10, 12, 10);
        panelIntro.WrapContents = false;
        //
        // lblIntroTitle
        //
        lblIntroTitle.AutoSize = true;
        lblIntroTitle.Name = "lblIntroTitle";
        lblIntroTitle.Text = "What does Copy do?";
        //
        // lblIntro
        //
        lblIntro.AutoSize = true;
        lblIntro.Margin = new Padding(3, 6, 3, 3);
        lblIntro.MaximumSize = new Size(800, 0);
        lblIntro.Name = "lblIntro";
        lblIntro.Text =
            "It makes ONE clean collection of all your files on a backup disk you choose, using what was found when you catalogued your disks.\n\n" +
            "•  Your source disks (the ones you catalogued) are only READ. Nothing on them is moved, changed or deleted.\n" +
            "•  Every unique file is copied once, sorted into Photos, Videos, Audio, Documents, Archives and Other. Inside those, the original folder names are kept.\n" +
            "•  Files that are exactly the same as one already copied go to the folder _Duplicates (only if there is room left). " +
            "Same name and folder but different content: the newest goes to the main folders, older ones to _OlderVersions. Nothing is thrown away.\n" +
            "•  Files that are already on the backup disk are skipped. Nothing on the backup disk is overwritten.\n" +
            "•  You can stop at any time. Starting again continues where it stopped.";
        //
        // lblIntroExample
        //
        lblIntroExample.AutoSize = true;
        lblIntroExample.Margin = new Padding(3, 8, 3, 3);
        lblIntroExample.MaximumSize = new Size(800, 0);
        lblIntroExample.Name = "lblIntroExample";
        lblIntroExample.Text =
            "Example:   F:\\Documenten\\muziek\\song.mp3   →   H:\\Audio\\Documenten\\muziek\\song.mp3   (H: = your backup disk)";
        //
        // lblIntroDeep
        //
        lblIntroDeep.AutoSize = true;
        lblIntroDeep.Margin = new Padding(3, 8, 3, 3);
        lblIntroDeep.MaximumSize = new Size(800, 0);
        lblIntroDeep.Name = "lblIntroDeep";
        lblIntroDeep.Text =
            "Copy only handles files you can already see on your disks. Getting back DELETED files from a damaged or formatted disk is " +
            "\"Deep recovery\" (coming next; for now the old PowerShell toolkit does it).";
        //
        // lblStep1
        //
        lblStep1.AutoSize = true;
        lblStep1.Margin = new Padding(3, 8, 3, 2);
        lblStep1.Name = "lblStep1";
        lblStep1.Text = "Step 1  —  Choose your backup disk";
        //
        // lblStep1Hint
        //
        lblStep1Hint.AutoSize = true;
        lblStep1Hint.MaximumSize = new Size(820, 0);
        lblStep1Hint.Name = "lblStep1Hint";
        lblStep1Hint.Text = "Pick the backup disk in the list on the right (or type/paste a folder path) and press Add. " +
            "If one disk is too small, add a second one: it is used when the first is full. Best is an empty disk. " +
            "On the Windows drive (C:) only a subfolder can be used, and at least 20 GB stays free.";
        //
        // panelDest
        //
        panelDest.AutoSize = true;
        panelDest.Controls.Add(lstDestinations);
        panelDest.Controls.Add(panelDestButtons);
        panelDest.Name = "panelDest";
        panelDest.WrapContents = false;
        //
        // lstDestinations
        //
        lstDestinations.IntegralHeight = false;
        lstDestinations.Name = "lstDestinations";
        lstDestinations.Size = new Size(520, 130);
        lstDestinations.TabIndex = 1;
        //
        // panelDestButtons
        //
        panelDestButtons.AutoSize = true;
        panelDestButtons.Controls.Add(cmbDrives);
        panelDestButtons.Controls.Add(btnAddDrive);
        panelDestButtons.Controls.Add(btnAddFolder);
        panelDestButtons.Controls.Add(btnUp);
        panelDestButtons.Controls.Add(btnDown);
        panelDestButtons.Controls.Add(btnRemove);
        panelDestButtons.Controls.Add(btnRefreshDrives);
        panelDestButtons.FlowDirection = FlowDirection.TopDown;
        panelDestButtons.Name = "panelDestButtons";
        //
        // cmbDrives
        //
        cmbDrives.DropDownStyle = ComboBoxStyle.DropDown;
        cmbDrives.Name = "cmbDrives";
        cmbDrives.Size = new Size(300, 28);
        cmbDrives.TabIndex = 2;
        //
        // btnAddDrive
        //
        btnAddDrive.Name = "btnAddDrive";
        btnAddDrive.TabIndex = 3;
        btnAddDrive.Text = "Add";
        //
        // btnAddFolder
        //
        btnAddFolder.Name = "btnAddFolder";
        btnAddFolder.TabIndex = 4;
        btnAddFolder.Text = "Add a folder…";
        //
        // btnUp
        //
        btnUp.Name = "btnUp";
        btnUp.TabIndex = 5;
        btnUp.Text = "Move up";
        //
        // btnDown
        //
        btnDown.Name = "btnDown";
        btnDown.TabIndex = 6;
        btnDown.Text = "Move down";
        //
        // btnRemove
        //
        btnRemove.Name = "btnRemove";
        btnRemove.TabIndex = 7;
        btnRemove.Text = "Remove";
        //
        // btnRefreshDrives
        //
        btnRefreshDrives.Name = "btnRefreshDrives";
        btnRefreshDrives.TabIndex = 8;
        btnRefreshDrives.Text = "Refresh drives";
        //
        // groupOptions
        //
        groupOptions.AutoSize = true;
        groupOptions.Controls.Add(flowOptions);
        groupOptions.Name = "groupOptions";
        groupOptions.Padding = new Padding(8);
        groupOptions.Size = new Size(820, 120);
        groupOptions.Text = "Options";
        //
        // flowOptions
        //
        flowOptions.AutoSize = true;
        flowOptions.Controls.Add(chkDuplicates);
        flowOptions.Controls.Add(chkVerify);
        flowOptions.Controls.Add(chkWait);
        flowOptions.Controls.Add(chkAllowPending);
        flowOptions.Controls.Add(lblReserve);
        flowOptions.Controls.Add(numReserve);
        flowOptions.Controls.Add(lblCap);
        flowOptions.Controls.Add(numCap);
        flowOptions.Dock = DockStyle.Fill;
        flowOptions.MaximumSize = new Size(800, 0);
        flowOptions.Name = "flowOptions";
        flowOptions.SetFlowBreak(chkDuplicates, true);
        flowOptions.SetFlowBreak(chkVerify, true);
        flowOptions.SetFlowBreak(chkWait, true);
        flowOptions.SetFlowBreak(chkAllowPending, true);
        flowOptions.SetFlowBreak(numReserve, true);
        //
        // chkDuplicates
        //
        chkDuplicates.AutoSize = true;
        chkDuplicates.Checked = true;
        chkDuplicates.Name = "chkDuplicates";
        chkDuplicates.Text = "Copy identical extra copies to _Duplicates (only into space that is left)";
        //
        // chkVerify
        //
        chkVerify.AutoSize = true;
        chkVerify.Name = "chkVerify";
        chkVerify.Text = "Read every copy back and compare (slower)";
        //
        // chkWait
        //
        chkWait.AutoSize = true;
        chkWait.Checked = true;
        chkWait.Name = "chkWait";
        chkWait.Text = "Wait up to 30 minutes when a disk is unplugged";
        //
        // chkAllowPending
        //
        chkAllowPending.AutoSize = true;
        chkAllowPending.Name = "chkAllowPending";
        chkAllowPending.Text = "Start even if some files are not fingerprinted (they count as unique)";
        //
        // lblReserve
        //
        lblReserve.AutoSize = true;
        lblReserve.Margin = new Padding(3, 8, 3, 0);
        lblReserve.Name = "lblReserve";
        lblReserve.Text = "Keep free on every backup disk (GB):";
        //
        // numReserve
        //
        numReserve.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
        numReserve.Name = "numReserve";
        numReserve.Size = new Size(80, 27);
        numReserve.Value = new decimal(new int[] { 1, 0, 0, 0 });
        //
        // lblCap
        //
        lblCap.AutoSize = true;
        lblCap.Margin = new Padding(16, 8, 3, 0);
        lblCap.Name = "lblCap";
        lblCap.Text = "Use at most on the selected backup disk (GB, 0 = no limit):";
        //
        // numCap
        //
        numCap.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
        numCap.Name = "numCap";
        numCap.Size = new Size(90, 27);
        //
        // lblStep2
        //
        lblStep2.AutoSize = true;
        lblStep2.Margin = new Padding(3, 18, 3, 2);
        lblStep2.Name = "lblStep2";
        lblStep2.Text = "Step 2  —  Preview (nothing is written yet)";
        //
        // lblStep2Hint
        //
        lblStep2Hint.AutoSize = true;
        lblStep2Hint.MaximumSize = new Size(820, 0);
        lblStep2Hint.Name = "lblStep2Hint";
        lblStep2Hint.Text = "Press \"Check the plan\": it shows how much will be copied, whether it fits, and where it goes. Nothing is written. You can do this as often as you like.";
        //
        // panelPlanButtons
        //
        panelPlanButtons.AutoSize = true;
        panelPlanButtons.Controls.Add(btnPlan);
        panelPlanButtons.Controls.Add(btnExport);
        panelPlanButtons.Name = "panelPlanButtons";
        //
        // btnPlan
        //
        btnPlan.Name = "btnPlan";
        btnPlan.TabIndex = 10;
        btnPlan.Text = "Check the plan";
        //
        // btnExport
        //
        btnExport.Name = "btnExport";
        btnExport.TabIndex = 11;
        btnExport.Text = "Save the full list (CSV)…";
        //
        // panelPlan
        //
        panelPlan.AutoSize = true;
        panelPlan.Controls.Add(flowTiles);
        panelPlan.Controls.Add(lblVerdict);
        panelPlan.Controls.Add(gridDestinations);
        panelPlan.Controls.Add(lblNotes);
        panelPlan.Controls.Add(panelDeletions);
        panelPlan.FlowDirection = FlowDirection.TopDown;
        panelPlan.Name = "panelPlan";
        panelPlan.WrapContents = false;
        //
        // flowTiles
        //
        flowTiles.AutoSize = true;
        flowTiles.Controls.Add(tileToCopy);
        flowTiles.Controls.Add(tileUnique);
        flowTiles.Controls.Add(tileDuplicates);
        flowTiles.Controls.Add(tileAlready);
        flowTiles.Name = "flowTiles";
        //
        // tileToCopy
        //
        tileToCopy.Caption = "files";
        tileToCopy.Name = "tileToCopy";
        tileToCopy.Title = "Will be copied";
        //
        // tileUnique
        //
        tileUnique.Caption = "one copy of all content";
        tileUnique.Name = "tileUnique";
        tileUnique.Title = "Unique data";
        //
        // tileDuplicates
        //
        tileDuplicates.Caption = "identical extra copies";
        tileDuplicates.Name = "tileDuplicates";
        tileDuplicates.Title = "Duplicates";
        //
        // tileAlready
        //
        tileAlready.Caption = "files";
        tileAlready.Name = "tileAlready";
        tileAlready.Title = "Already there";
        //
        // lblVerdict
        //
        lblVerdict.AutoSize = true;
        lblVerdict.Margin = new Padding(3, 10, 3, 6);
        lblVerdict.MaximumSize = new Size(820, 0);
        lblVerdict.Name = "lblVerdict";
        //
        // gridDestinations
        //
        gridDestinations.Name = "gridDestinations";
        gridDestinations.Size = new Size(820, 130);
        gridDestinations.TabIndex = 12;
        //
        // lblNotes
        //
        lblNotes.AutoSize = true;
        lblNotes.Margin = new Padding(3, 6, 3, 3);
        lblNotes.MaximumSize = new Size(820, 0);
        lblNotes.Name = "lblNotes";
        //
        // panelDeletions
        //
        panelDeletions.AutoSize = true;
        panelDeletions.Controls.Add(lblDeletions);
        panelDeletions.Controls.Add(btnReviewDeletions);
        panelDeletions.FlowDirection = FlowDirection.TopDown;
        panelDeletions.Name = "panelDeletions";
        //
        // lblDeletions
        //
        lblDeletions.AutoSize = true;
        lblDeletions.MaximumSize = new Size(820, 0);
        lblDeletions.Name = "lblDeletions";
        //
        // btnReviewDeletions
        //
        btnReviewDeletions.Name = "btnReviewDeletions";
        btnReviewDeletions.TabIndex = 13;
        btnReviewDeletions.Text = "Review and approve the deletions…";
        //
        // lblStep3
        //
        lblStep3.AutoSize = true;
        lblStep3.Margin = new Padding(3, 18, 3, 2);
        lblStep3.Name = "lblStep3";
        lblStep3.Text = "Step 3  —  Start copying";
        //
        // lblSafe
        //
        lblSafe.AutoSize = true;
        lblSafe.MaximumSize = new Size(820, 0);
        lblSafe.Name = "lblSafe";
        lblSafe.Text = "Your source disks are only read. Nothing on the backup disk is overwritten. Stop any time: starting again continues where it stopped.";
        //
        // panelStart
        //
        panelStart.AutoSize = true;
        panelStart.Controls.Add(btnStart);
        panelStart.Controls.Add(btnStop);
        panelStart.Name = "panelStart";
        //
        // btnStart
        //
        btnStart.Enabled = false;
        btnStart.Name = "btnStart";
        btnStart.TabIndex = 14;
        btnStart.Text = "Start copying";
        //
        // btnStop
        //
        btnStop.Name = "btnStop";
        btnStop.TabIndex = 15;
        btnStop.Text = "Stop";
        //
        // panelProgress
        //
        panelProgress.AutoSize = true;
        panelProgress.Controls.Add(lblStage);
        panelProgress.Controls.Add(progress);
        panelProgress.Controls.Add(lblDetail);
        panelProgress.Controls.Add(lblResult);
        panelProgress.FlowDirection = FlowDirection.TopDown;
        panelProgress.Name = "panelProgress";
        panelProgress.WrapContents = false;
        //
        // lblStage
        //
        lblStage.AutoSize = true;
        lblStage.Name = "lblStage";
        //
        // progress
        //
        progress.Name = "progress";
        progress.Size = new Size(820, 18);
        //
        // lblDetail
        //
        lblDetail.AutoSize = true;
        lblDetail.Name = "lblDetail";
        //
        // lblResult
        //
        lblResult.AutoSize = true;
        lblResult.Margin = new Padding(3, 8, 3, 3);
        lblResult.MaximumSize = new Size(820, 0);
        lblResult.Name = "lblResult";
        //
        // CopyView
        //
        AutoScaleDimensions = new SizeF(8F, 20F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(scroll);
        Name = "CopyView";
        Size = new Size(900, 900);
        panelIntro.ResumeLayout(false);
        panelIntro.PerformLayout();
        layout.ResumeLayout(false);
        layout.PerformLayout();
        scroll.ResumeLayout(false);
        scroll.PerformLayout();
        panelDest.ResumeLayout(false);
        panelDest.PerformLayout();
        panelDestButtons.ResumeLayout(false);
        groupOptions.ResumeLayout(false);
        groupOptions.PerformLayout();
        flowOptions.ResumeLayout(false);
        flowOptions.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)numReserve).EndInit();
        ((System.ComponentModel.ISupportInitialize)numCap).EndInit();
        panelPlanButtons.ResumeLayout(false);
        panelPlanButtons.PerformLayout();
        panelPlan.ResumeLayout(false);
        panelPlan.PerformLayout();
        flowTiles.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)gridDestinations).EndInit();
        panelDeletions.ResumeLayout(false);
        panelDeletions.PerformLayout();
        panelStart.ResumeLayout(false);
        panelStart.PerformLayout();
        panelProgress.ResumeLayout(false);
        panelProgress.PerformLayout();
        ResumeLayout(false);
    }

    #endregion

    private Panel scroll;
    private TableLayoutPanel layout;
    private FlowLayoutPanel panelIntro;
    private Label lblIntroTitle;
    private Label lblIntro;
    private Label lblIntroExample;
    private Label lblIntroDeep;
    private Label lblStep2Hint;
    private Label lblStep1;
    private Label lblStep1Hint;
    private FlowLayoutPanel panelDest;
    private ListBox lstDestinations;
    private FlowLayoutPanel panelDestButtons;
    private ComboBox cmbDrives;
    private Button btnAddDrive;
    private Button btnAddFolder;
    private Button btnUp;
    private Button btnDown;
    private Button btnRemove;
    private Button btnRefreshDrives;
    private GroupBox groupOptions;
    private FlowLayoutPanel flowOptions;
    private CheckBox chkDuplicates;
    private CheckBox chkVerify;
    private CheckBox chkWait;
    private CheckBox chkAllowPending;
    private Label lblReserve;
    private NumericUpDown numReserve;
    private Label lblCap;
    private NumericUpDown numCap;
    private Label lblStep2;
    private FlowLayoutPanel panelPlanButtons;
    private Button btnPlan;
    private Button btnExport;
    private FlowLayoutPanel panelPlan;
    private FlowLayoutPanel flowTiles;
    private Controls.StatTile tileToCopy;
    private Controls.StatTile tileUnique;
    private Controls.StatTile tileDuplicates;
    private Controls.StatTile tileAlready;
    private Label lblVerdict;
    private DataGridView gridDestinations;
    private Label lblNotes;
    private FlowLayoutPanel panelDeletions;
    private Label lblDeletions;
    private Button btnReviewDeletions;
    private Label lblStep3;
    private Label lblSafe;
    private FlowLayoutPanel panelStart;
    private Button btnStart;
    private Button btnStop;
    private FlowLayoutPanel panelProgress;
    private Label lblStage;
    private ProgressBar progress;
    private Label lblDetail;
    private Label lblResult;
}
