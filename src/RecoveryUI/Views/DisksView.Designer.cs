namespace RecoveryUI.Views;

partial class DisksView
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
        panelTop = new FlowLayoutPanel();
        lblDrives = new Label();
        btnRefresh = new Button();
        lblHint = new Label();
        gridDrives = new DataGridView();
        lblCatalog = new Label();
        gridCatalog = new DataGridView();
        lblDevices = new Label();
        gridDevices = new DataGridView();
        lblLinux = new Label();
        panelLinux = new FlowLayoutPanel();
        lblDisk = new Label();
        numDisk = new NumericUpDown();
        lblPartition = new Label();
        numPartition = new NumericUpDown();
        lblFs = new Label();
        cmbFs = new ComboBox();
        btnLinuxScan = new Button();
        lblLinuxStatus = new Label();
        layout.SuspendLayout();
        panelTop.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)gridDrives).BeginInit();
        ((System.ComponentModel.ISupportInitialize)gridCatalog).BeginInit();
        ((System.ComponentModel.ISupportInitialize)gridDevices).BeginInit();
        panelLinux.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)numDisk).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numPartition).BeginInit();
        SuspendLayout();
        //
        // layout
        //
        layout.AutoScroll = true;
        layout.ColumnCount = 1;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(panelTop, 0, 0);
        layout.Controls.Add(lblHint, 0, 1);
        layout.Controls.Add(gridDrives, 0, 2);
        layout.Controls.Add(lblCatalog, 0, 3);
        layout.Controls.Add(gridCatalog, 0, 4);
        layout.Controls.Add(lblDevices, 0, 5);
        layout.Controls.Add(gridDevices, 0, 6);
        layout.Controls.Add(lblLinux, 0, 7);
        layout.Controls.Add(panelLinux, 0, 8);
        layout.Dock = DockStyle.Fill;
        layout.Name = "layout";
        layout.RowCount = 9;
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 200F));
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 140F));
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 180F));
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        //
        // panelTop
        //
        panelTop.AutoSize = true;
        panelTop.Controls.Add(lblDrives);
        panelTop.Controls.Add(btnRefresh);
        panelTop.Margin = new Padding(0);
        panelTop.Name = "panelTop";
        //
        // lblDrives
        //
        lblDrives.AutoSize = true;
        lblDrives.Margin = new Padding(0, 8, 16, 0);
        lblDrives.Name = "lblDrives";
        lblDrives.Text = "Plugged-in drives";
        //
        // btnRefresh
        //
        btnRefresh.Name = "btnRefresh";
        btnRefresh.Text = "Refresh";
        //
        // lblHint
        //
        lblHint.AutoSize = true;
        lblHint.Margin = new Padding(0, 0, 0, 6);
        lblHint.MaximumSize = new Size(900, 0);
        lblHint.Name = "lblHint";
        lblHint.Text = "Connect the disk you recover FROM directly to a USB port of the PC (not a hub or dock); put backup disks in the powered dock. \"Connection\" shows how Windows sees each disk: USB, SATA, NVMe, SD card...";
        //
        // gridDrives
        //
        gridDrives.Dock = DockStyle.Fill;
        gridDrives.Name = "gridDrives";
        //
        // lblCatalog
        //
        lblCatalog.AutoSize = true;
        lblCatalog.Margin = new Padding(0, 14, 0, 6);
        lblCatalog.Name = "lblCatalog";
        lblCatalog.Text = "Disks in the catalog";
        //
        // gridCatalog
        //
        gridCatalog.Dock = DockStyle.Fill;
        gridCatalog.Name = "gridCatalog";
        //
        // lblDevices
        //
        lblDevices.AutoSize = true;
        lblDevices.Margin = new Padding(0, 14, 0, 6);
        lblDevices.Name = "lblDevices";
        lblDevices.Text = "Physical disks";
        //
        // gridDevices
        //
        gridDevices.Dock = DockStyle.Fill;
        gridDevices.Name = "gridDevices";
        //
        // lblLinux
        //
        lblLinux.AutoSize = true;
        lblLinux.Margin = new Padding(0, 14, 0, 6);
        lblLinux.Name = "lblLinux";
        lblLinux.Text = "Linux disks (ext4) via WSL";
        //
        // panelLinux
        //
        panelLinux.AutoSize = true;
        panelLinux.Controls.Add(lblDisk);
        panelLinux.Controls.Add(numDisk);
        panelLinux.Controls.Add(lblPartition);
        panelLinux.Controls.Add(numPartition);
        panelLinux.Controls.Add(lblFs);
        panelLinux.Controls.Add(cmbFs);
        panelLinux.Controls.Add(btnLinuxScan);
        panelLinux.Controls.Add(lblLinuxStatus);
        panelLinux.Margin = new Padding(0);
        panelLinux.Name = "panelLinux";
        //
        // lblDisk
        //
        lblDisk.AutoSize = true;
        lblDisk.Margin = new Padding(0, 8, 4, 0);
        lblDisk.Name = "lblDisk";
        lblDisk.Text = "Disk #";
        //
        // numDisk
        //
        numDisk.Maximum = new decimal(new int[] { 63, 0, 0, 0 });
        numDisk.Name = "numDisk";
        numDisk.Size = new Size(56, 25);
        //
        // lblPartition
        //
        lblPartition.AutoSize = true;
        lblPartition.Margin = new Padding(12, 8, 4, 0);
        lblPartition.Name = "lblPartition";
        lblPartition.Text = "Partition";
        //
        // numPartition
        //
        numPartition.Maximum = new decimal(new int[] { 128, 0, 0, 0 });
        numPartition.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        numPartition.Name = "numPartition";
        numPartition.Size = new Size(56, 25);
        numPartition.Value = new decimal(new int[] { 1, 0, 0, 0 });
        //
        // lblFs
        //
        lblFs.AutoSize = true;
        lblFs.Margin = new Padding(12, 8, 4, 0);
        lblFs.Name = "lblFs";
        lblFs.Text = "File system";
        //
        // cmbFs
        //
        cmbFs.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbFs.Name = "cmbFs";
        cmbFs.Size = new Size(80, 25);
        //
        // btnLinuxScan
        //
        btnLinuxScan.Margin = new Padding(16, 0, 3, 3);
        btnLinuxScan.Name = "btnLinuxScan";
        btnLinuxScan.Text = "Mount read-only and catalog";
        //
        // lblLinuxStatus
        //
        lblLinuxStatus.AutoSize = true;
        lblLinuxStatus.Margin = new Padding(12, 8, 0, 0);
        lblLinuxStatus.MaximumSize = new Size(520, 0);
        lblLinuxStatus.Name = "lblLinuxStatus";
        lblLinuxStatus.Text = "Checking WSL…";
        //
        // DisksView
        //
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(layout);
        Name = "DisksView";
        Size = new Size(940, 640);
        layout.ResumeLayout(false);
        layout.PerformLayout();
        panelTop.ResumeLayout(false);
        panelTop.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)gridDrives).EndInit();
        ((System.ComponentModel.ISupportInitialize)gridCatalog).EndInit();
        ((System.ComponentModel.ISupportInitialize)gridDevices).EndInit();
        panelLinux.ResumeLayout(false);
        panelLinux.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)numDisk).EndInit();
        ((System.ComponentModel.ISupportInitialize)numPartition).EndInit();
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel layout;
    private FlowLayoutPanel panelTop;
    private Label lblDrives;
    private Button btnRefresh;
    private Label lblHint;
    private DataGridView gridDrives;
    private Label lblCatalog;
    private DataGridView gridCatalog;
    private Label lblDevices;
    private DataGridView gridDevices;
    private Label lblLinux;
    private FlowLayoutPanel panelLinux;
    private Label lblDisk;
    private NumericUpDown numDisk;
    private Label lblPartition;
    private NumericUpDown numPartition;
    private Label lblFs;
    private ComboBox cmbFs;
    private Button btnLinuxScan;
    private Label lblLinuxStatus;
}
