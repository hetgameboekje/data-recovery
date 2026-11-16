using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;

namespace RecoveryBLL.Fingerprinting;

/// <summary>What the structure of an MP4/MOV says about it (port of media_cleanup.py video_status).</summary>
public enum VideoStatus
{
    /// <summary>Header, picture+sound (mdat) and index (moov) are there and fill the file exactly: it plays.</summary>
    Complete,
    /// <summary>Complete, but the tag block at the end (uuid/XMP) is damaged: it still plays, only metadata is lost.</summary>
    TagsDamaged,
    /// <summary>Not an MP4/MOV structure (or unreadable): no judgement.</summary>
    Unknown,
    /// <summary>Picture data or index missing, or the file is cut off / has data after its last box.</summary>
    Broken,
}

/// <summary>
/// Reads the top-level boxes of an MP4/MOV (only box headers: a few reads per file).
/// Lessons from 2026-11-12 (docs/cleanup-session-2026-11-12.md):
///   - a damaged tag block is NOT a broken video (11 playable videos were sent to review);
///   - the same video can carry more, fewer, no or damaged tags at the end; <see cref="MediaHash"/> ignores them.
/// </summary>
public static class Mp4Structure
{
    public readonly record struct Box(string Type, long Offset, long Size);

    static readonly string[] StartTypes = ["ftyp", "wide", "mdat", "moov", "free", "skip"];
    static readonly string[] MediaTypes = ["ftyp", "mdat", "moov"];   // header, picture+sound, index: what makes the video

    /// <summary>Top-level boxes, or null when the file does not start like an MP4/MOV.</summary>
    public static IReadOnlyList<Box>? Boxes(Stream s)
    {
        long size = s.Length, pos = 0;
        var boxes = new List<Box>();
        Span<byte> h = stackalloc byte[16];
        while (pos + 8 <= size)
        {
            s.Seek(pos, SeekOrigin.Begin);
            if (s.ReadAtLeast(h[..8], 8, throwOnEndOfStream: false) < 8) break;
            long n = BinaryPrimitives.ReadUInt32BigEndian(h);
            var type = Encoding.Latin1.GetString(h.Slice(4, 4));
            if (n == 1)
            {
                if (s.ReadAtLeast(h.Slice(8, 8), 8, throwOnEndOfStream: false) < 8) break;
                n = (long)BinaryPrimitives.ReadUInt64BigEndian(h.Slice(8, 8));
            }
            else if (n == 0) n = size - pos;
            if (n < 8 || !type.All(c => c >= 0x20 && c < 0x7F)) break;
            boxes.Add(new Box(type, pos, n));
            pos += n;
        }
        return boxes.Count == 0 || !StartTypes.Contains(boxes[0].Type) ? null : boxes;
    }

    public static VideoStatus Status(Stream s)
    {
        var boxes = Boxes(s);
        if (boxes == null)
        {
            // PhotoRec also writes pieces that start inside 'mdat' or 'wide' (f123_mdat.mov), or a few bytes only
            if (s.Length < 64) return VideoStatus.Broken;
            return VideoStatus.Unknown;
        }
        var last = boxes[^1];
        if (!boxes.Any(b => b.Type == "mdat") || !boxes.Any(b => b.Type == "moov") || last.Offset + last.Size != s.Length)
            return VideoStatus.Broken;
        if (last.Type == "uuid")
        {
            var start = Math.Max(last.Offset, s.Length - 256);
            s.Seek(start, SeekOrigin.Begin);
            var tail = new byte[s.Length - start];
            s.ReadExactly(tail);
            if (tail.AsSpan().IndexOf("<?xpacket end="u8) < 0) return VideoStatus.TagsDamaged;
        }
        return VideoStatus.Complete;
    }

    /// <summary>Size of the trailing tag block(s) (uuid/XMP): more = more tags.</summary>
    public static long TagBytes(IReadOnlyList<Box>? boxes) => boxes?.Where(b => b.Type == "uuid").Sum(b => b.Size) ?? 0;

    /// <summary>Sizes of ftyp/mdat/moov. Two files can only be the same video when their shapes are equal. Null: not a complete-enough MP4.</summary>
    public static string? MediaShape(IReadOnlyList<Box>? boxes)
    {
        if (boxes == null || !boxes.Any(b => b.Type == "mdat") || !boxes.Any(b => b.Type == "moov")) return null;
        return string.Join(",", MediaTypes.Select(t => boxes.Where(b => b.Type == t).Sum(b => b.Size)));
    }

    /// <summary>
    /// xxHash128 over the ftyp, mdat and moov boxes only; trailing tag blocks are ignored. Equal = the same video byte for
    /// byte, even when one copy has more, fewer, no or damaged tags. Throws <see cref="IOException"/> when cut off.
    /// </summary>
    public static byte[] MediaHash(Stream s, IReadOnlyList<Box> boxes, CancellationToken ct = default)
    {
        var x = new XxHash128();
        var buf = new byte[1024 * 1024];
        foreach (var b in boxes.Where(b => MediaTypes.Contains(b.Type)))
        {
            x.Append(Encoding.ASCII.GetBytes(b.Type));
            s.Seek(b.Offset, SeekOrigin.Begin);
            long left = b.Size;
            while (left > 0)
            {
                ct.ThrowIfCancellationRequested();
                int n = s.Read(buf, 0, (int)Math.Min(buf.Length, left));
                if (n <= 0) throw new IOException($"cut off inside '{b.Type}'");
                x.Append(buf.AsSpan(0, n));
                left -= n;
            }
        }
        return x.GetCurrentHash();
    }
}

/// <summary>
/// Is a picture complete? Structure only (no decoding, no extra libraries): a JPEG must end with its end marker, a PNG with
/// its IEND chunk, a GIF with its trailer. Catches cut-off pictures (most of the "does not open" photos on 2026-11-12).
/// Other formats (HEIC, WebP, TIFF, BMP...) are not judged.
/// </summary>
public static class ImageIntegrity
{
    /// <summary>null = complete or not judged; otherwise why it is broken.</summary>
    public static string? Problem(Stream s, string ext)
    {
        long size = s.Length;
        if (size < 16) return "only a few bytes";
        var head = new byte[8];
        s.Seek(0, SeekOrigin.Begin);
        s.ReadAtLeast(head, 8, throwOnEndOfStream: false);
        int tailLen = (int)Math.Min(size, 64 * 1024);
        var tail = new byte[tailLen];
        s.Seek(size - tailLen, SeekOrigin.Begin);
        s.ReadExactly(tail);
        switch (ext)
        {
            case "jpg" or "jpeg" or "jpe" or "jfif":
                if (head[0] != 0xFF || head[1] != 0xD8) return "not a JPEG (no start marker)";
                return tail.AsSpan().IndexOf((ReadOnlySpan<byte>)[0xFF, 0xD9]) < 0 ? "cut off: the JPEG has no end marker" : null;
            case "png":
                if (head[0] != 0x89 || head[1] != (byte)'P') return "not a PNG (no signature)";
                return tail.AsSpan().IndexOf("IEND"u8) < 0 ? "cut off: the PNG has no end chunk" : null;
            case "gif":
                if (head[0] != (byte)'G' || head[1] != (byte)'I') return "not a GIF (no signature)";
                return tail.AsSpan().TrimEnd((byte)0).EndsWith((ReadOnlySpan<byte>)[0x3B]) ? null : "cut off: the GIF has no trailer";
            default:
                return null;
        }
    }
}
