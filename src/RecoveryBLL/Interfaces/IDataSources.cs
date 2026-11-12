using RecoveryBLL.Models;

namespace RecoveryBLL.Interfaces;

// Non-database data sources the BLL depends on. Implemented in the DAL (Windows APIs, file system, WSL, XML).

public interface IVolumeProvider
{
    VolumeIdentity? Identify(string root);
    IReadOnlyList<VolumeIdentity> GetMounted();
    long GetFreeBytes(string root);
}

/// <summary>Physical disks and their bus (USB, SATA, NVMe, SD, ...). Works without admin rights.</summary>
public interface IPhysicalDeviceProvider
{
    PhysicalDevice? GetDeviceForVolume(string root);
    IReadOnlyList<PhysicalDevice> GetAll();
}

public sealed record WalkedFile(string FullPath, string Name, long Size, long MtimeTicks, bool InRoot);

public interface IFileSystemWalker
{
    /// <summary>
    /// Yields every file under <paramref name="folder"/>. Folders whose name is in <paramref name="skipFolderNames"/>
    /// or whose full path is in <paramref name="skipFolderPaths"/> are not entered; junctions/symlinks are never followed.
    /// Unreadable folders are reported through <paramref name="onFolderError"/>.
    /// </summary>
    IEnumerable<WalkedFile> Walk(string folder, IReadOnlySet<string> skipFolderNames, IReadOnlyCollection<string> skipFolderPaths,
                                 Action<string, Exception> onFolderError, CancellationToken ct);
}

public interface IFileContentReader
{
    /// <summary>Open for reading while allowing others to read/write/delete. <paramref name="sequential"/> hints a full read.</summary>
    Stream OpenRead(string fullPath, bool sequential);
}

/// <summary>Loads the lookup lists (config/dictionaries.xml).</summary>
public interface IDictionaryRepository
{
    string Location { get; }
    RecoveryDictionaries Load();
}

/// <summary>Where the services write what they did. The frontends decide how to show it.</summary>
public interface IOperationLog
{
    void Info(string message);
    void Warn(string message);
}

/// <summary>Windows Subsystem for Linux: mounting ext4/btrfs/xfs disks so they can be scanned like a folder.</summary>
public interface IWslGateway
{
    bool IsInstalled();
    IReadOnlyList<string> GetDistributions();
    /// <summary>wsl --mount, read-only. Needs an elevated (administrator) process. Returns the Windows path of the mount.</summary>
    string MountReadOnly(int diskNumber, int partition, string fileSystem);
    void Unmount(int diskNumber);
    /// <summary>Filesystem UUID and size of a mounted path, used as its catalog identity.</summary>
    VolumeIdentity? GetMountIdentity(string windowsPath, int diskNumber, int partition, string fileSystem);
}

public interface IElevationInfo
{
    bool IsElevated { get; }
}
