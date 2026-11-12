using System.Text;
using RecoveryBLL.Fingerprinting;

namespace RecoveryBLL.Tests;

public class ContentHasherTests
{
    [Fact]
    public void Small_file_quick_hash_is_also_the_full_hash()
    {
        var data = TestHost.Random(5000, 1);
        var r = ContentHasher.Quick(1, new MemoryStream(data), data.Length, sha256: true);
        Assert.NotNull(r.FullHash);
        Assert.Equal(r.QuickHash, r.FullHash);
        Assert.Equal(ContentHasher.Full(1, new MemoryStream(data), data.Length, false).FullHash, r.FullHash);
        Assert.Equal(32, r.Sha256!.Length);
    }

    [Fact]
    public void Middle_difference_passes_quick_hash_but_not_full_hash()
    {
        var a = TestHost.Random(300_000, 2);
        var b = (byte[])a.Clone();
        b[150_000] ^= 0xFF;   // same size, same first/last 64 KB
        var qa = ContentHasher.Quick(1, new MemoryStream(a), a.Length, false);
        var qb = ContentHasher.Quick(2, new MemoryStream(b), b.Length, false);
        Assert.Equal(qa.QuickHash, qb.QuickHash);
        Assert.Null(qa.FullHash);
        Assert.NotEqual(ContentHasher.Full(1, new MemoryStream(a), a.Length, false).FullHash,
                        ContentHasher.Full(2, new MemoryStream(b), b.Length, false).FullHash);
    }

    [Fact]
    public void Changed_size_is_reported()
    {
        var data = TestHost.Random(1000, 3);
        Assert.Throws<FileChangedException>(() => ContentHasher.Quick(1, new MemoryStream(data), 999, false));
    }
}

public class ImageHeaderReaderTests
{
    [Fact]
    public void Reads_png_dimensions()
    {
        var png = new byte[33];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 0, 64, 0, 0, 0, 32 }.CopyTo(png, 0);
        var info = ImageHeaderReader.Read(new MemoryStream(png), "png");
        Assert.Equal((64, 32), (info.Width, info.Height));
    }

    [Fact]
    public void Reads_jpeg_size_and_exif_camera_and_date()
    {
        var info = ImageHeaderReader.Read(new MemoryStream(BuildJpeg()), "jpg");
        Assert.Equal((4000, 3000), (info.Width, info.Height));
        Assert.Equal("Canon EOS 600D", info.Camera);
        Assert.Equal("2019-07-14 18:30:05", info.Taken);
    }

    [Fact]
    public void Garbage_gives_no_dimensions_instead_of_crashing()
    {
        var info = ImageHeaderReader.Read(new MemoryStream(TestHost.Random(500, 4)), "jpg");
        Assert.Null(info.Width);
    }

    /// <summary>JPEG: SOI, APP1/Exif (Make, Model, ExifIFD -> DateTimeOriginal), SOF0 4000x3000, SOS, EOI.</summary>
    internal static byte[] BuildJpeg()
    {
        static byte[] Le16(int v) => BitConverter.GetBytes((ushort)v);
        static byte[] Le32(int v) => BitConverter.GetBytes(v);
        var make = Encoding.ASCII.GetBytes("Canon\0");
        var model = Encoding.ASCII.GetBytes("Canon EOS 600D\0");
        var date = Encoding.ASCII.GetBytes("2019:07:14 18:30:05\0");
        int ifd0 = 8, ifd0Len = 2 + 3 * 12 + 4;
        int makeOff = ifd0 + ifd0Len, modelOff = makeOff + make.Length, exifOff = modelOff + model.Length;
        int dateOff = exifOff + 2 + 12 + 4;
        var t = new List<byte>();
        t.AddRange("II*\0"u8.ToArray()); t.AddRange(Le32(8));
        t.AddRange(Le16(3));
        t.AddRange(Le16(0x010F)); t.AddRange(Le16(2)); t.AddRange(Le32(make.Length)); t.AddRange(Le32(makeOff));
        t.AddRange(Le16(0x0110)); t.AddRange(Le16(2)); t.AddRange(Le32(model.Length)); t.AddRange(Le32(modelOff));
        t.AddRange(Le16(0x8769)); t.AddRange(Le16(4)); t.AddRange(Le32(1)); t.AddRange(Le32(exifOff));
        t.AddRange(Le32(0));
        t.AddRange(make); t.AddRange(model);
        t.AddRange(Le16(1));
        t.AddRange(Le16(0x9003)); t.AddRange(Le16(2)); t.AddRange(Le32(date.Length)); t.AddRange(Le32(dateOff));
        t.AddRange(Le32(0));
        t.AddRange(date);
        var app1 = "Exif\0\0"u8.ToArray().Concat(t).ToArray();
        var j = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE1, (byte)((app1.Length + 2) >> 8), (byte)(app1.Length + 2) };
        j.AddRange(app1);
        j.AddRange(new byte[] { 0xFF, 0xC0, 0, 17, 8, 0x0B, 0xB8, 0x0F, 0xA0, 3 });   // height 3000, width 4000
        j.AddRange(new byte[9]);
        j.AddRange(new byte[] { 0xFF, 0xDA, 0, 2, 0xFF, 0xD9 });
        return j.ToArray();
    }
}
