using System.Runtime.InteropServices;
using System.Text;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;

namespace RecoveryDAL.Storage;

public sealed class WindowsVolumeProvider : IVolumeProvider
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetVolumeInformationW(string rootPathName, StringBuilder volumeNameBuffer, int volumeNameSize,
        out uint volumeSerialNumber, out uint maximumComponentLength, out uint fileSystemFlags,
        StringBuilder fileSystemNameBuffer, int nFileSystemNameSize);

    public VolumeIdentity? Identify(string root)
    {
        if (!root.EndsWith('\\')) root += "\\";
        var name = new StringBuilder(261);
        var fs = new StringBuilder(261);
        if (!GetVolumeInformationW(root, name, name.Capacity, out uint serial, out _, out _, fs, fs.Capacity))
            return null;
        long total = 0;
        try { total = new DriveInfo(root).TotalSize; } catch (Exception ex) when (ex is IOException or ArgumentException) { }
        return new VolumeIdentity(serial.ToString("X8"), total, name.ToString(), fs.ToString(), root);
    }

    public IReadOnlyList<VolumeIdentity> GetMounted()
    {
        var list = new List<VolumeIdentity>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.DriveType == DriveType.Network) continue;   // network shares: not supported yet
                if (Identify(d.RootDirectory.FullName) is { } v) list.Add(v);
            }
            catch (IOException) { /* drive vanished while listing */ }
        }
        return list;
    }

    public long GetFreeBytes(string root)
    {
        try { return new DriveInfo(root).AvailableFreeSpace; }
        catch (Exception ex) when (ex is IOException or ArgumentException) { return 0; }
    }
}
