using RecoveryUI.Infrastructure;
using RecoveryUI.ViewModels;

namespace RecoveryUI.Views;

/// <summary>Which drives are plugged in and how they are connected; in Expert mode also physical disks and Linux (WSL) mounts.</summary>
public partial class DisksView : UserControl, IDisksView
{
    public event EventHandler? RefreshRequested;
    public event EventHandler<LinuxScanRequest>? LinuxScanRequested;
    public Control Control => this;
    bool expert;

    public DisksView()
    {
        InitializeComponent();
        foreach (var g in new[] { gridDrives, gridCatalog, gridDevices }) UiTheme.Grid(g);
        foreach (var l in new[] { lblDrives, lblCatalog, lblDevices, lblLinux }) l.Font = UiTheme.HeadingFont;
        lblHint.ForeColor = UiTheme.Muted;
        lblLinuxStatus.ForeColor = UiTheme.Muted;
        UiTheme.Secondary(btnRefresh);
        UiTheme.Primary(btnLinuxScan);
        cmbFs.Items.AddRange(["ext4", "ext3", "ext2", "btrfs", "xfs"]);
        cmbFs.SelectedIndex = 0;
        btnRefresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        btnLinuxScan.Click += (_, _) => LinuxScanRequested?.Invoke(this,
            new LinuxScanRequest((int)numDisk.Value, (int)numPartition.Value, (string)cmbFs.SelectedItem!));
        SetExpertMode(false);
    }

    public void ShowDrives(IReadOnlyList<DriveRowViewModel> drives) => this.OnUi(() => GridBinder.Bind(gridDrives, drives, expert));
    public void ShowCatalogued(IReadOnlyList<CatalogDiskRowViewModel> disks) => this.OnUi(() => GridBinder.Bind(gridCatalog, disks, expert));
    public void ShowDevices(IReadOnlyList<DeviceRowViewModel> devices) => this.OnUi(() => GridBinder.Bind(gridDevices, devices, expert));

    public void ShowLinuxStatus(string status, bool canMount) => this.OnUi(() =>
    {
        lblLinuxStatus.Text = status;
        btnLinuxScan.Enabled = canMount;
    });

    public void SetExpertMode(bool expert)
    {
        this.expert = expert;
        lblDevices.Visible = gridDevices.Visible = expert;
        lblLinux.Visible = panelLinux.Visible = expert;
        GridBinder.ApplyExpert<DriveRowViewModel>(gridDrives, expert);
        GridBinder.ApplyExpert<CatalogDiskRowViewModel>(gridCatalog, expert);
    }
}
