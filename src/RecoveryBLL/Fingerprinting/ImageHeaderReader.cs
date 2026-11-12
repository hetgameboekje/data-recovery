using System.Buffers.Binary;
using System.Text;

namespace RecoveryBLL.Fingerprinting;

/// <summary>
/// Reads image dimensions and EXIF camera/date straight from the file header (a few KB), without decoding.
/// Needed later for junk detection (skins, icons) and for spotting low-quality previews (phase 3/3b).
/// Pure parsing: it gets a stream, so it is testable without files.
/// </summary>
public static class ImageHeaderReader
{
    public sealed record Info(int? Width, int? Height, string? Camera, string? Taken);

    public static Info Read(Stream fs, string ext)
    {
        return ext switch
        {
            "jpg" or "jpeg" or "jpe" or "jfif" => Jpeg(fs),
            "png" => Png(fs),
            "gif" => Gif(fs),
            "bmp" => Bmp(fs),
            "webp" => WebP(fs),
            _ => new Info(null, null, null, null),
        };
    }

    static byte[] Head(Stream s, int n)
    {
        var buf = new byte[n];
        int got = s.ReadAtLeast(buf, n, throwOnEndOfStream: false);
        return got == n ? buf : buf[..got];
    }

    static Info Png(Stream s)
    {
        var b = Head(s, 24);
        if (b.Length < 24 || b[0] != 0x89 || b[1] != (byte)'P' || Encoding.ASCII.GetString(b, 12, 4) != "IHDR")
            return new(null, null, null, null);
        return new((int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(16)), (int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(20)), null, null);
    }

    static Info Gif(Stream s)
    {
        var b = Head(s, 10);
        if (b.Length < 10 || b[0] != 'G' || b[1] != 'I' || b[2] != 'F') return new(null, null, null, null);
        return new(BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(6)), BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(8)), null, null);
    }

    static Info Bmp(Stream s)
    {
        var b = Head(s, 26);
        if (b.Length < 26 || b[0] != 'B' || b[1] != 'M') return new(null, null, null, null);
        return new(Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(18))), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(22))), null, null);
    }

    static Info WebP(Stream s)
    {
        var b = Head(s, 30);
        if (b.Length < 30 || Encoding.ASCII.GetString(b, 0, 4) != "RIFF" || Encoding.ASCII.GetString(b, 8, 4) != "WEBP")
            return new(null, null, null, null);
        switch (Encoding.ASCII.GetString(b, 12, 4))
        {
            case "VP8 ":
                return new(BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(26)) & 0x3FFF,
                           BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(28)) & 0x3FFF, null, null);
            case "VP8L":
                return new(1 + (b[21] | ((b[22] & 0x3F) << 8)),
                           1 + ((b[22] >> 6) | (b[23] << 2) | ((b[24] & 0x0F) << 10)), null, null);
            case "VP8X":
                return new(1 + (b[24] | (b[25] << 8) | (b[26] << 16)),
                           1 + (b[27] | (b[28] << 8) | (b[29] << 16)), null, null);
            default:
                return new(null, null, null, null);
        }
    }

    static Info Jpeg(Stream s)
    {
        if (s.ReadByte() != 0xFF || s.ReadByte() != 0xD8) return new(null, null, null, null);
        string? camera = null, taken = null;
        for (int guard = 0; guard < 4096; guard++)
        {
            int b = s.ReadByte();
            if (b < 0) break;
            if (b != 0xFF) continue;                       // resync on garbage
            int marker;
            do { marker = s.ReadByte(); } while (marker == 0xFF);
            if (marker < 0 || marker == 0xD9 || marker == 0xDA) break;   // end of image / start of scan data
            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) continue; // no length field
            int hi = s.ReadByte(), lo = s.ReadByte();
            if (hi < 0 || lo < 0) break;
            int len = (hi << 8) | lo;
            if (len < 2) break;

            bool sof = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
            if (sof)
            {
                var h = Head(s, 5);
                if (h.Length < 5) break;
                int height = (h[1] << 8) | h[2], width = (h[3] << 8) | h[4];
                return new(width, height, camera, taken);
            }
            if (marker == 0xE1 && camera == null && taken == null)
            {
                var seg = Head(s, len - 2);
                if (seg.Length >= 14 && Encoding.ASCII.GetString(seg, 0, 4) == "Exif")
                    (camera, taken) = Exif(seg.AsSpan(6));
                continue;
            }
            s.Seek(len - 2, SeekOrigin.Current);
        }
        return new(null, null, camera, taken);
    }

    /// <summary>Minimal TIFF/EXIF reader: Make, Model (IFD0) and DateTimeOriginal (Exif IFD).</summary>
    static (string? camera, string? taken) Exif(ReadOnlySpan<byte> t)
    {
        try
        {
            if (t.Length < 8) return (null, null);
            bool le = t[0] == 'I' && t[1] == 'I';
            if (!le && !(t[0] == 'M' && t[1] == 'M')) return (null, null);
            uint U32(ReadOnlySpan<byte> x, int o) => le ? BinaryPrimitives.ReadUInt32LittleEndian(x[o..]) : BinaryPrimitives.ReadUInt32BigEndian(x[o..]);
            ushort U16(ReadOnlySpan<byte> x, int o) => le ? BinaryPrimitives.ReadUInt16LittleEndian(x[o..]) : BinaryPrimitives.ReadUInt16BigEndian(x[o..]);

            string? make = null, model = null, taken = null;
            uint exifIfd = 0;

            string? Ascii(ReadOnlySpan<byte> x, int entry)
            {
                if (U16(x, entry + 2) != 2) return null;
                int count = (int)U32(x, entry + 4);
                int off = count <= 4 ? entry + 8 : (int)U32(x, entry + 8);
                if (count <= 0 || off < 0 || off + count > x.Length) return null;
                var s = Encoding.ASCII.GetString(x.Slice(off, count)).TrimEnd('\0', ' ');
                return s.Length == 0 ? null : s;
            }

            int ifd0 = (int)U32(t, 4);
            if (ifd0 + 2 <= t.Length)
            {
                int n = U16(t, ifd0);
                for (int i = 0; i < n && ifd0 + 2 + i * 12 + 12 <= t.Length; i++)
                {
                    int e = ifd0 + 2 + i * 12;
                    switch (U16(t, e))
                    {
                        case 0x010F: make = Ascii(t, e); break;
                        case 0x0110: model = Ascii(t, e); break;
                        case 0x8769: exifIfd = U32(t, e + 8); break;
                    }
                }
            }
            if (exifIfd > 0 && exifIfd + 2 <= t.Length)
            {
                int ei = (int)exifIfd;
                int n = U16(t, ei);
                for (int i = 0; i < n && ei + 2 + i * 12 + 12 <= t.Length; i++)
                {
                    int e = ei + 2 + i * 12;
                    if (U16(t, e) == 0x9003) { taken = Ascii(t, e); break; }
                }
            }

            string? camera = model == null ? make
                : make == null || model.StartsWith(make, StringComparison.OrdinalIgnoreCase) ? model
                : $"{make} {model}";
            // EXIF "yyyy:MM:dd HH:mm:ss" -> "yyyy-MM-dd HH:mm:ss"
            if (taken is { Length: >= 19 } && taken[4] == ':' && taken[7] == ':')
                taken = $"{taken[..4]}-{taken[5..7]}-{taken[8..10]}{taken[10..19]}";
            if (taken != null && (taken.StartsWith("0000") || taken.Trim().Length == 0)) taken = null;
            return (camera, taken);
        }
        catch (ArgumentOutOfRangeException) { return (null, null); }
    }
}
