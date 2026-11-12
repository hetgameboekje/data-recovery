using RecoveryBLL.Exceptions;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;

namespace RecoveryBLL.Services;

/// <summary>
/// Linux disks (ext4, and btrfs/xfs where the WSL kernel supports them) through WSL 2.
/// The partition is mounted READ-ONLY, then scanned like any folder. The catalog identifies it by its
/// filesystem UUID, because Windows reports the same "volume" for every path under \\wsl.localhost.
/// </summary>
public sealed class LinuxDiskService(IWslGateway wsl, IElevationInfo elevation, DiskService disks, RunStateService runState, IOperationLog log)
{
    public static readonly string[] SupportedFileSystems = ["ext4", "ext3", "ext2", "btrfs", "xfs"];

    public bool IsWslInstalled => wsl.IsInstalled();
    public bool IsElevated => elevation.IsElevated;
    public IReadOnlyList<string> Distributions => wsl.IsInstalled() ? wsl.GetDistributions() : [];

    public LinuxMount Mount(int diskNumber, int partition, string fileSystem)
    {
        if (!SupportedFileSystems.Contains(fileSystem))
            throw new RecoveryServiceException($"Unsupported filesystem '{fileSystem}'. Use one of: {string.Join(", ", SupportedFileSystems)}.");
        if (diskNumber < 0 || partition < 1)
            throw new RecoveryServiceException("Choose a physical disk number (0 or higher) and a partition (1 or higher).");
        if (!wsl.IsInstalled())
            throw new RecoveryServiceException("WSL 2 is not installed. Install it with:  wsl --install");
        if (!elevation.IsElevated)
            throw new RecoveryServiceException("Mounting a Linux disk needs administrator rights. Start the toolkit as administrator.");

        // Journal first: if this process dies while the disk is attached, the next start unmounts it.
        runState.Hold(LeaseKind.WslMount, diskNumber.ToString());
        string path;
        try { path = wsl.MountReadOnly(diskNumber, partition, fileSystem); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            runState.Release(LeaseKind.WslMount, diskNumber.ToString());
            throw new RecoveryServiceException($"wsl --mount failed: {ex.Message}", ex);
        }

        var identity = wsl.GetMountIdentity(path, diskNumber, partition, fileSystem)
            ?? throw new RecoveryServiceException("Mounted, but could not read the filesystem UUID. Unmount and try again.");
        var diskId = disks.EnsureDisk(identity);
        disks.RegisterExtraRoot(diskId, identity.Root);
        log.Info($"Mounted PhysicalDrive{diskNumber} partition {partition} ({fileSystem}) read-only at {path}");
        return new LinuxMount(diskNumber, partition, fileSystem, path, identity);
    }

    public void Unmount(LinuxMount mount)
    {
        try { wsl.Unmount(mount.DiskNumber); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            throw new RecoveryServiceException($"wsl --unmount failed: {ex.Message}", ex);
        }
        runState.Release(LeaseKind.WslMount, mount.DiskNumber.ToString());
        var id = disks.EnsureDisk(mount.Identity);
        disks.RemoveExtraRoot(id);
        log.Info($"Unmounted PhysicalDrive{mount.DiskNumber}");
    }
}
