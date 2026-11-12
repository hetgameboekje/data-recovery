using System.Collections.Concurrent;
using RecoveryBLL.Dtos;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;

namespace RecoveryBLL.Services;

/// <summary>Knows which disks are catalogued, which are plugged in, and how each is connected.</summary>
public sealed class DiskService(
    IDiskRepository disks,
    IVolumeProvider volumes,
    IPhysicalDeviceProvider devices,
    DictionaryService dictionaries)
{
    // Disks reachable through a path Windows does not see as a volume (e.g. a WSL ext4 mount): disk id -> root.
    readonly ConcurrentDictionary<long, string> extraRoots = new();

    public long EnsureDisk(VolumeIdentity v) => RepositoryGuard.Run(() =>
    {
        var existing = disks.FindByIdentity(v.Serial, v.TotalBytes);
        if (existing != null)
        {
            if (v.Root.Length > 0) disks.UpdateSeen(existing.Id, v.Label, v.FileSystem, v.Root);   // merges carry no drive letter
            return existing.Id;
        }
        return disks.Add(new DiskDto
        {
            Serial = v.Serial,
            TotalBytes = v.TotalBytes,
            Label = v.Label,
            FileSystem = v.FileSystem,
            LastRoot = v.Root,
            FirstSeen = RepositoryGuard.Now(),
        });
    }, "Cannot register the disk in the catalog");

    public void MarkScanCompleted(long diskId, string stamp) =>
        RepositoryGuard.Run(() => disks.MarkScanCompleted(diskId, stamp), "Cannot update the disk");

    public void RegisterExtraRoot(long diskId, string root) => extraRoots[diskId] = root;
    public void RemoveExtraRoot(long diskId) => extraRoots.TryRemove(diskId, out _);

    public IReadOnlyList<Disk> GetCatalogued() =>
        RepositoryGuard.Run(() => disks.GetAll().Select(d => new Disk(d)).ToList(), "Cannot read the disk list");

    public Disk? Get(long id) => RepositoryGuard.Run(() => disks.GetById(id) is { } d ? new Disk(d) : null, "Cannot read the disk");

    /// <summary>Catalogued disks that are reachable right now: disk id -> current root (e.g. "G:\").</summary>
    public IReadOnlyDictionary<long, string> GetMountedRoots()
    {
        var map = new Dictionary<long, string>();
        foreach (var v in volumes.GetMounted())
        {
            var d = RepositoryGuard.Run(() => disks.FindByIdentity(v.Serial, v.TotalBytes), "Cannot read the disk list");
            if (d == null || map.ContainsKey(d.Id)) continue;
            map[d.Id] = v.Root;
            if (d.LastRoot != v.Root || d.Label != v.Label)
                RepositoryGuard.Run(() => disks.UpdateSeen(d.Id, v.Label, v.FileSystem, v.Root), "Cannot update the disk");
        }
        foreach (var (id, root) in extraRoots)
            if (Directory.Exists(root)) map.TryAdd(id, root);
        return map;
    }

    public IReadOnlyList<DriveStatus> GetPluggedInDrives()
    {
        var known = GetCatalogued().ToDictionary(d => (d.Serial, d.TotalBytes));
        var list = new List<DriveStatus>();
        foreach (var v in volumes.GetMounted())
        {
            var device = SafeDevice(v.Root);
            var bus = device != null && dictionaries.Current.BusTypes.TryGetValue(device.Bus, out var b) ? b : null;
            known.TryGetValue((v.Serial, v.TotalBytes), out var disk);
            list.Add(new DriveStatus(v, volumes.GetFreeBytes(v.Root), device, bus, disk, RecommendedThreads(device)));
        }
        return list;
    }

    public PhysicalDevice? GetDeviceForRoot(string root) => SafeDevice(root);

    public IReadOnlyList<PhysicalDevice> GetPhysicalDevices()
    {
        try { return devices.GetAll(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>
    /// Readers per disk while hashing. Spinning disks get 1 (parallel reads only make them seek);
    /// flash gets the value from dictionaries.xml for its bus (NVMe high, USB bridges lower).
    /// </summary>
    public int RecommendedThreads(PhysicalDevice? device)
    {
        if (device == null || device.IncursSeekPenalty != false) return 1;
        return dictionaries.Current.BusTypes.TryGetValue(device.Bus, out var info) ? Math.Max(1, info.ReadThreads) : 2;
    }

    PhysicalDevice? SafeDevice(string root)
    {
        try { return devices.GetDeviceForVolume(root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
