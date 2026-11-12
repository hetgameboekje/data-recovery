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

/// <summary>
/// Writing to the destination disks (phase 2). Copies are written to a temporary ".rtpart" file and renamed
/// when complete, so an interrupted copy never looks like a finished file.
/// </summary>
public interface IFileStore
{
    /// <summary>Creates parent folders and opens a new (or truncated) file for writing.</summary>
    Stream CreateTemp(string fullPath);
    /// <summary>Called after the stream is closed: sets the modified time and renames. Never overwrites an existing file.</summary>
    void Commit(string tempPath, string finalPath, long mtimeTicksUtc);
    bool Exists(string fullPath);
    /// <summary>Modified time (UTC ticks) as the file system stores it.</summary>
    long LastWriteTicks(string fullPath);
    long Length(string fullPath);
    void Delete(string fullPath);
    /// <summary>Half-written copies (*.rtpart) under a folder. Links are not followed; unreadable folders are skipped.</summary>
    IReadOnlyList<string> FindTemps(string folder);
}

/// <summary>
/// Folders the toolkit must never scan or write to, because doing so can harm or stall Windows
/// (Windows, Program Files, ProgramData, every user's AppData, ...). Implemented per OS in the DAL.
/// </summary>
public interface IProtectedLocations
{
    /// <summary>Root of the drive Windows runs from, e.g. "C:\".</summary>
    string SystemDrive { get; }
    /// <summary>Nothing inside these may be used, and no folder that contains one of them either.</summary>
    IReadOnlyList<string> NoGoFolders { get; }
    /// <summary>Full path with short (8.3) names expanded, links/junctions resolved and no trailing separator (except a root).</summary>
    string Normalize(string path);
}

/// <summary>The run-state journal (which runs hold which mounts/destinations). Shared by every process on this PC.</summary>
public interface IRunStateStore
{
    string Location { get; }
    /// <summary>Atomic read-modify-write under a lock. <paramref name="change"/> edits the list in place.</summary>
    IReadOnlyList<RunLease> Update(Action<List<RunLease>> change);
}

public interface IProcessInfo
{
    int CurrentId { get; }
    long CurrentStartTicks { get; }
    /// <summary>True if that process (same id AND same start time) is still running. Unknown counts as alive.</summary>
    bool IsAlive(int processId, long startTicks);
}
