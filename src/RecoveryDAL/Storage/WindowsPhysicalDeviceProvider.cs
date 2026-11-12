using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;

namespace RecoveryDAL.Storage;

/// <summary>
/// Asks Windows how each disk is connected (USB, SATA, NVMe, SD, ...) and whether it spins.
/// Uses IOCTLs that need no read access, so it works without administrator rights.
/// </summary>
public sealed class WindowsPhysicalDeviceProvider : IPhysicalDeviceProvider
{
    const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
    const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;
    const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x000700A0;
    const int StorageDeviceProperty = 0, StorageDeviceSeekPenaltyProperty = 7, StorageDeviceTrimProperty = 8;
    const uint FILE_SHARE_READ_WRITE = 0x3, OPEN_EXISTING = 3;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[]? inBuffer, int inSize,
        byte[] outBuffer, int outSize, out int bytesReturned, IntPtr overlapped);

    public PhysicalDevice? GetDeviceForVolume(string root)
    {
        if (root.Length < 2 || root[1] != ':') return null;                 // only drive letters have disk extents
        using var vol = Open($@"\\.\{root[0]}:");
        if (vol == null) return null;
        var buf = new byte[256];
        if (!DeviceIoControl(vol, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, null, 0, buf, buf.Length, out _, IntPtr.Zero))
            return null;                                                     // e.g. RAM disks, some virtual drives
        // VOLUME_DISK_EXTENTS: DWORD count; (4 bytes padding); DISK_EXTENT { DWORD DiskNumber; ... }
        if (BinaryPrimitives.ReadInt32LittleEndian(buf) < 1) return null;
        return GetDevice(BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(8)));
    }

    public IReadOnlyList<PhysicalDevice> GetAll()
    {
        var list = new List<PhysicalDevice>();
        for (int n = 0; n < 64; n++)
            if (GetDevice(n) is { } d) list.Add(d);
        return list;
    }

    static PhysicalDevice? GetDevice(int number)
    {
        using var h = Open($@"\\.\PhysicalDrive{number}");
        if (h == null) return null;

        var desc = Query(h, StorageDeviceProperty, 1024);
        if (desc == null || desc.Length < 36) return null;
        // STORAGE_DEVICE_DESCRIPTOR: Version, Size, DeviceType(8), Modifier(9), RemovableMedia(10), CommandQueueing(11),
        // VendorIdOffset(12), ProductIdOffset(16), ProductRevisionOffset(20), SerialNumberOffset(24), BusType(28)
        bool removable = desc[10] != 0;
        string vendor = Ascii(desc, BinaryPrimitives.ReadInt32LittleEndian(desc.AsSpan(12)));
        string product = Ascii(desc, BinaryPrimitives.ReadInt32LittleEndian(desc.AsSpan(16)));
        string serial = Ascii(desc, BinaryPrimitives.ReadInt32LittleEndian(desc.AsSpan(24)));
        int bus = BinaryPrimitives.ReadInt32LittleEndian(desc.AsSpan(28));

        var seek = Query(h, StorageDeviceSeekPenaltyProperty, 12);
        var trim = Query(h, StorageDeviceTrimProperty, 12);

        long size = 0;
        var geo = new byte[256];
        if (DeviceIoControl(h, IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, null, 0, geo, geo.Length, out var got, IntPtr.Zero) && got >= 32)
            size = BinaryPrimitives.ReadInt64LittleEndian(geo.AsSpan(24));  // DISK_GEOMETRY (24 bytes) then DiskSize

        var model = string.Join(' ', new[] { vendor, product }.Where(s => s.Length > 0));
        return new PhysicalDevice(
            number,
            model.Length > 0 ? model : $"PhysicalDrive{number}",
            serial.Length > 0 ? serial : null,
            Enum.IsDefined(typeof(BusType), bus) ? (BusType)bus : BusType.Unknown,
            seek is { Length: >= 9 } ? seek[8] != 0 : null,
            trim is { Length: >= 9 } ? trim[8] != 0 : null,
            removable,
            size);
    }

    static byte[]? Query(SafeFileHandle h, int propertyId, int outSize)
    {
        var query = new byte[12];                                            // STORAGE_PROPERTY_QUERY
        BinaryPrimitives.WriteInt32LittleEndian(query, propertyId);
        BinaryPrimitives.WriteInt32LittleEndian(query.AsSpan(4), 0);         // PropertyStandardQuery
        var output = new byte[outSize];
        return DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, query, query.Length, output, output.Length, out var n, IntPtr.Zero) && n > 0
            ? output[..n] : null;
    }

    static SafeFileHandle? Open(string device)
    {
        var h = CreateFileW(device, 0, FILE_SHARE_READ_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);  // access 0: metadata only
        if (!h.IsInvalid) return h;
        h.Dispose();
        return null;
    }

    static string Ascii(byte[] buf, int offset)
    {
        if (offset <= 0 || offset >= buf.Length) return "";
        int end = Array.IndexOf(buf, (byte)0, offset);
        if (end < 0) end = buf.Length;
        return Encoding.ASCII.GetString(buf, offset, end - offset).Trim();
    }
}
