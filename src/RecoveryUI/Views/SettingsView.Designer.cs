namespace RecoveryUI.Views;

partial class SettingsView
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
        lblDbTitle = new Label();
        lblDb = new Label();
        lblDbHelp = new Label();
        panelDbButtons = new FlowLayoutPanel();
        btnOpenConfig = new Button();
        btnMerge = new Button();
        lblDictTitle = new Label();
        lblDict = new Label();
        lblDictHelp = new Label();
        btnReload = new Button();
        lblLogTitle = new Label();
        lblLog = new Label();
        btnOpenLogs = new Button();
        lblNetTitle = new Label();
        lblNet = new Label();
        lblNetHelp = new Label();
        layout.SuspendLayout();
        panelDbButtons.SuspendLayout();
        SuspendLayout();
        //
        // layout
        //
        layout.AutoScroll = true;
        layout.ColumnCount = 1;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(lblDbTitle, 0, 0);
        layout.Controls.Add(lblDb, 0, 1);
        layout.Controls.Add(lblDbHelp, 0, 2);
        layout.Controls.Add(panelDbButtons, 0, 3);
        layout.Controls.Add(lblDictTitle, 0, 4);
        layout.Controls.Add(lblDict, 0, 5);
        layout.Controls.Add(lblDictHelp, 0, 6);
        layout.Controls.Add(btnReload, 0, 7);
        layout.Controls.Add(lblLogTitle, 0, 8);
        layout.Controls.Add(lblLog, 0, 9);
        layout.Controls.Add(btnOpenLogs, 0, 10);
        layout.Controls.Add(lblNetTitle, 0, 11);
        layout.Controls.Add(lblNet, 0, 12);
        layout.Controls.Add(lblNetHelp, 0, 13);
        layout.Dock = DockStyle.Fill;
        layout.Name = "layout";
        layout.RowCount = 15;
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
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        //
        // lblDbTitle
        //
        lblDbTitle.AutoSize = true;
        lblDbTitle.Margin = new Padding(0, 0, 0, 4);
        lblDbTitle.Name = "lblDbTitle";
        lblDbTitle.Text = "Catalog database";
        //
        // lblDb
        //
        lblDb.AutoSize = true;
        lblDb.Name = "lblDb";
        lblDb.Text = "…";
        //
        // lblDbHelp
        //
        lblDbHelp.AutoSize = true;
        lblDbHelp.MaximumSize = new Size(900, 0);
        lblDbHelp.Name = "lblDbHelp";
        lblDbHelp.Text = "Set DB_PROVIDER (sqlite or sqlserver) and DB_CONNECTION in the .env file, then restart. Passwords belong in .env (never in code); the file is not committed to git.";
        //
        // panelDbButtons
        //
        panelDbButtons.AutoSize = true;
        panelDbButtons.Controls.Add(btnOpenConfig);
        panelDbButtons.Controls.Add(btnMerge);
        panelDbButtons.Margin = new Padding(0, 6, 0, 18);
        panelDbButtons.Name = "panelDbButtons";
        //
        // btnOpenConfig
        //
        btnOpenConfig.Name = "btnOpenConfig";
        btnOpenConfig.Text = "Open settings folder";
        //
        // btnMerge
        //
        btnMerge.Name = "btnMerge";
        btnMerge.Text = "Merge a catalog from another PC…";
        //
        // lblDictTitle
        //
        lblDictTitle.AutoSize = true;
        lblDictTitle.Margin = new Padding(0, 0, 0, 4);
        lblDictTitle.Name = "lblDictTitle";
        lblDictTitle.Text = "Dictionaries (XML)";
        //
        // lblDict
        //
        lblDict.AutoSize = true;
        lblDict.Name = "lblDict";
        lblDict.Text = "…";
        //
        // lblDictHelp
        //
        lblDictHelp.AutoSize = true;
        lblDictHelp.MaximumSize = new Size(900, 0);
        lblDictHelp.Name = "lblDictHelp";
        lblDictHelp.Text = "File types per extension, folders to skip, connection types and junk rules. Edit the XML file, then reload.";
        //
        // btnReload
        //
        btnReload.Margin = new Padding(0, 6, 0, 18);
        btnReload.Name = "btnReload";
        btnReload.Text = "Reload dictionaries";
        //
        // lblLogTitle
        //
        lblLogTitle.AutoSize = true;
        lblLogTitle.Margin = new Padding(0, 0, 0, 4);
        lblLogTitle.Name = "lblLogTitle";
        lblLogTitle.Text = "Logs";
        //
        // lblLog
        //
        lblLog.AutoSize = true;
        lblLog.Name = "lblLog";
        lblLog.Text = "…";
        //
        // btnOpenLogs
        //
        btnOpenLogs.Margin = new Padding(0, 6, 0, 18);
        btnOpenLogs.Name = "btnOpenLogs";
        btnOpenLogs.Text = "Open log folder";
        //
        // lblNetTitle
        //
        lblNetTitle.AutoSize = true;
        lblNetTitle.Margin = new Padding(0, 0, 0, 4);
        lblNetTitle.Name = "lblNetTitle";
        lblNetTitle.Text = "Network export (planned)";
        //
        // lblNet
        //
        lblNet.AutoSize = true;
        lblNet.Name = "lblNet";
        lblNet.Text = "…";
        //
        // lblNetHelp
        //
        lblNetHelp.AutoSize = true;
        lblNetHelp.MaximumSize = new Size(900, 0);
        lblNetHelp.Name = "lblNetHelp";
        lblNetHelp.Text = "Later: recovered files go to a backlog folder; a separate uploader sends them over SFTP or FTP. Credentials and the SSH key path go in .env (NET_*).";
        //
        // SettingsView
        //
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(layout);
        Name = "SettingsView";
        Size = new Size(940, 640);
        layout.ResumeLayout(false);
        layout.PerformLayout();
        panelDbButtons.ResumeLayout(false);
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel layout;
    private Label lblDbTitle;
    private Label lblDb;
    private Label lblDbHelp;
    private FlowLayoutPanel panelDbButtons;
    private Button btnOpenConfig;
    private Button btnMerge;
    private Label lblDictTitle;
    private Label lblDict;
    private Label lblDictHelp;
    private Button btnReload;
    private Label lblLogTitle;
    private Label lblLog;
    private Button btnOpenLogs;
    private Label lblNetTitle;
    private Label lblNet;
    private Label lblNetHelp;
}
