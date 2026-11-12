using System.Runtime.InteropServices;
using System.Text;

namespace RecoveryCatalog;

/// <summary>A volume identified independently of its drive letter.</summary>
sealed record VolumeId(string Serial, long TotalBytes, string Label, string FileSystem, string Root);

static class Volumes
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetVolumeInformationW(string rootPathName, StringBuilder volumeNameBuffer, int volumeNameSize,
        out uint volumeSerialNumber, out uint maximumComponentLength, out uint fileSystemFlags,
        StringBuilder fileSystemNameBuffer, int nFileSystemNameSize);

    public static VolumeId? Identify(string root)
    {
        if (!root.EndsWith('\\')) root += "\\";
        var name = new StringBuilder(261);
        var fs = new StringBuilder(261);
        if (!GetVolumeInformationW(root, name, name.Capacity, out uint serial, out _, out _, fs, fs.Capacity))
            return null;
        long total = 0;
        try { total = new DriveInfo(root).TotalSize; } catch { /* UNC shares etc. */ }
        return new VolumeId(serial.ToString("X8"), total, name.ToString(), fs.ToString(), root);
    }

    public static List<VolumeId> Mounted()
    {
        var list = new List<VolumeId>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                var v = Identify(d.RootDirectory.FullName);
                if (v != null) list.Add(v);
            }
            catch { /* drive vanished while listing */ }
        }
        return list;
    }
}
