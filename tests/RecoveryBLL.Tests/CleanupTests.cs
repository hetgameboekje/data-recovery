using System.Buffers.Binary;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using RecoveryBLL.Exceptions;
using RecoveryBLL.Fingerprinting;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;
using RecoveryBLL.Services;

namespace RecoveryBLL.Tests;

/// <summary>
/// The Clean up page, against real files in a temp folder. Every case here is one that went wrong on 2026-11-12
/// (docs/cleanup-session-2026-11-12.md) or a rule the user set.
/// </summary>
public class CleanupTests
{
    // ---- synthetic MP4s: ftyp + mdat (picture/sound) + moov (index) [+ uuid tag block]

    static byte[] Box(string type, byte[] body)
    {
        var b = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(b, (uint)b.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(b, 4);
        body.CopyTo(b, 8);
        return b;
    }

    /// <param name="tags">null: no tag block; otherwise the XMP text (a complete block ends with the xpacket end tag).</param>
    static byte[] Mp4(int seed, int mediaSize = 200_000, string? tags = null, bool withIndex = true)
    {
        var parts = new List<byte[]> { Box("ftyp", Encoding.ASCII.GetBytes("isom\0\0\0\0isomavc1")), Box("mdat", TestHost.Random(mediaSize, seed)) };
        if (withIndex) parts.Add(Box("moov", TestHost.Random(500, seed + 1000)));
        if (tags != null) parts.Add(Box("uuid", Encoding.UTF8.GetBytes(new string(' ', 16) + tags)));
        return parts.SelectMany(p => p).ToArray();
    }

    const string GoodTags = "<x:xmpmeta>lightroom tags</x:xmpmeta><?xpacket end=\"w\"?>";
    const string FewerTags = "<x:xmpmeta/><?xpacket end=\"w\"?>";
    static string Damaged => "<x:xmpmeta>lightroom tags</x:xmp" + new string('\u0001', 20);

    static byte[] Jpeg(int seed, bool complete = true) =>
        [0xFF, 0xD8, 0xFF, 0xE0, .. TestHost.Random(30_000, seed), .. (complete ? new byte[] { 0xFF, 0xD9 } : [])];

    static byte[] Png(int w, int h, int size)
    {
        var b = new byte[size];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(b, 0);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(16), (uint)w);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(20), (uint)h);
        "IEND"u8.CopyTo(b.AsSpan(size - 8));
        return b;
    }

    static readonly IReadOnlySet<CleanupCategory> All = CleanupCategories.All.ToHashSet();

    static (TempDir, ServiceProvider, TestConfirmer) Host(bool confirm = true)
    {
        var d = new TempDir();
        var confirmer = new TestConfirmer(confirm);
        var sp = TestHost.Create(d.Path, new ListLog(), configure: s => s.AddSingleton<IDeletionConfirmer>(confirmer));
        return (d, sp, confirmer);
    }

    static string P(TempDir d, params string[] parts) => Path.Combine([d.Path, .. parts]);

    [Fact]
    public void Video_structure_damaged_tags_are_not_broken_and_tags_do_not_change_the_media_hash()
    {
        static (VideoStatus, string?, byte[]) Read(byte[] file)
        {
            using var s = new MemoryStream(file);
            var boxes = Mp4Structure.Boxes(s)!;
            return (Mp4Structure.Status(s), Mp4Structure.MediaShape(boxes), Mp4Structure.MediaHash(s, boxes));
        }
        var good = Read(Mp4(1, tags: GoodTags));
        var none = Read(Mp4(1));
        var damaged = Read(Mp4(1, tags: Damaged));
        Assert.Equal(VideoStatus.Complete, good.Item1);
        Assert.Equal(VideoStatus.Complete, none.Item1);
        Assert.Equal(VideoStatus.TagsDamaged, damaged.Item1);                         // lesson: damaged tags != broken video
        Assert.Equal(VideoStatus.Broken, Read(Mp4(1, withIndex: false)).Item1);       // no index: broken
        Assert.Equal(good.Item3, none.Item3);
        Assert.Equal(good.Item3, damaged.Item3);                                     // same video whatever the tags
        Assert.NotEqual(good.Item3, Read(Mp4(2)).Item3);
        using var tiny = new MemoryStream(new byte[24]);
        Assert.Equal(VideoStatus.Broken, Mp4Structure.Status(tiny));
    }

    [Fact]
    public void Clean_a_folder_moves_only_the_unwanted_files_into_category_then_type_and_undo_puts_everything_back()
    {
        var (d, sp, _) = Host();
        using var _1 = d;
        using var _2 = sp;
        var v = P(d, "Videos");
        d.File(@"Videos\f100.mp4", Mp4(1, tags: GoodTags));          // the best copy: complete, most tags
        d.File(@"Videos\f050.mp4", Mp4(1));                          // same video, no tags (lower number, but fewer tags)
        d.File(@"Videos\f300.mp4", Mp4(1, tags: Damaged));           // same video, damaged tags
        d.File(@"Videos\f400.mp4", Mp4(1, tags: GoodTags));          // byte-for-byte copy of f100
        d.File(@"Videos\f500.mp4", Mp4(5, mediaSize: 300_000, tags: Damaged));   // unique, damaged tags: a playable video, stays
        d.File(@"Videos\clip.mp4", Mp4(6, mediaSize: 20_000));       // a complete 20 KB clip: a real video, stays
        d.File(@"Videos\f600.mp4", Mp4(7, withIndex: false));        // no index: broken
        d.File(@"Videos\scrap.mp4", new byte[24]);                   // 24 bytes: broken
        d.File(@"Videos\photo.jpg", Jpeg(8));
        d.File(@"Videos\photo copy.jpg", Jpeg(8));
        d.File(@"Videos\cut.jpg", Jpeg(9, complete: false));
        d.File(@"Videos\tiny.png", Png(37, 23, 500));
        d.File(@"Videos\Thumbs.db", TestHost.Random(3000, 10));
        var target = P(d, "duplicates");
        var clean = sp.GetRequiredService<CleanupService>();

        var plan = clean.Find(new CleanupOptions(v, target, null, All), null, CancellationToken.None);
        var byName = plan.Items.ToDictionary(i => Path.GetFileName(i.SourcePath));
        Assert.Equal(CleanupCategory.ExactCopy, byName["f400.mp4"].Category);
        Assert.Equal(CleanupCategory.SameVideo, byName["f050.mp4"].Category);
        Assert.Equal(CleanupCategory.SameVideo, byName["f300.mp4"].Category);
        Assert.EndsWith("f100.mp4", byName["f050.mp4"].Keep);
        Assert.Equal(CleanupCategory.Broken, byName["f600.mp4"].Category);
        Assert.Equal(CleanupCategory.Broken, byName["scrap.mp4"].Category);
        Assert.Equal(CleanupCategory.ExactCopy, byName["photo copy.jpg"].Category);
        Assert.Equal(CleanupCategory.Broken, byName["cut.jpg"].Category);
        Assert.Equal(CleanupCategory.SmallFile, byName["tiny.png"].Category);
        Assert.Equal(CleanupCategory.CachesAndSystem, byName["Thumbs.db"].Category);
        foreach (var stays in new[] { "f100.mp4", "f500.mp4", "clip.mp4", "photo.jpg" })
            Assert.False(byName.ContainsKey(stays), stays + " must stay");
        Assert.Equal(Path.Combine(target, "Same video", "Videos", "f050.mp4"), byName["f050.mp4"].DestPath);
        Assert.True(File.Exists(P(d, @"Videos\f050.mp4")));                            // Find changes nothing
        Assert.False(Directory.Exists(target));

        var r = clean.Execute(plan, plan.Items, null, CancellationToken.None);
        Assert.Equal(plan.Items.Count, r.Moved);
        Assert.Equal(0, r.Errors);
        Assert.Equal(["clip.mp4", "f100.mp4", "f500.mp4", "photo.jpg"], Directory.GetFiles(v).Select(Path.GetFileName).Order());
        Assert.True(File.Exists(Path.Combine(target, "Exact copies", "Videos", "f400.mp4")));
        Assert.True(File.Exists(Path.Combine(target, "Broken", "Photos", "cut.jpg")));
        Assert.True(File.Exists(Path.Combine(target, "Small files", "Photos", "tiny.png")));
        Assert.Equal(FileAttributes.Directory, File.GetAttributes(target) & (FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System));
        Assert.NotNull(r.LogFile);

        Assert.Equal(plan.Items.Count, clean.LastRun()!.Value.Files);
        var u = clean.UndoLast(null, CancellationToken.None);
        Assert.Equal(plan.Items.Count, u.Moved);
        Assert.Equal(13, Directory.GetFiles(v).Length);
        Assert.Null(clean.LastRun());                                                 // nothing left to undo
        Assert.True(Directory.Exists(target));                                       // the user's folder is never removed
    }

    [Fact]
    public void Merge_keeps_what_is_in_the_master_moves_new_files_in_and_copies_out_then_removes_the_empty_source()
    {
        var (d, sp, _) = Host();
        using var _1 = d;
        using var _2 = sp;
        d.File(@"G\Videos\master.mp4", Mp4(1));                      // in the master: no tags, but it ALWAYS stays
        d.File(@"G\From_E\Videos\a.mp4", Mp4(1, tags: GoodTags));    // same video with more tags: still goes to the target
        d.File(@"G\From_E\_Duplicates\Videos\b.mp4", Mp4(1));        // exact copy of the master
        d.File(@"G\From_E\Videos\new.mp4", Mp4(3));                  // not in the master yet
        d.File(@"G\From_E\Photos\p.jpg", Jpeg(4));
        var clean = sp.GetRequiredService<CleanupService>();
        var plan = clean.Find(new CleanupOptions(P(d, "G", "From_E"), P(d, "G", "duplicates"), P(d, "G"), All), null, CancellationToken.None);
        var byName = plan.Items.ToDictionary(i => Path.GetFileName(i.SourcePath));
        Assert.Equal(CleanupCategory.SameVideo, byName["a.mp4"].Category);
        Assert.Equal(CleanupCategory.ExactCopy, byName["b.mp4"].Category);
        Assert.EndsWith("master.mp4", byName["a.mp4"].Keep);
        Assert.Equal(P(d, "G", "Videos", "new.mp4"), byName["new.mp4"].DestPath);
        Assert.Equal(P(d, "G", "Photos", "p.jpg"), byName["p.jpg"].DestPath);
        Assert.DoesNotContain(plan.Items, i => i.SourcePath.EndsWith("master.mp4"));

        clean.Execute(plan, plan.Items, null, CancellationToken.None);
        Assert.True(File.Exists(P(d, @"G\Videos\master.mp4")));
        Assert.True(File.Exists(P(d, @"G\Videos\new.mp4")));
        Assert.True(File.Exists(P(d, @"G\Photos\p.jpg")));
        Assert.True(File.Exists(P(d, @"G\duplicates\Same video\Videos\a.mp4")));
        Assert.False(Directory.Exists(P(d, "G", "From_E")));                        // emptied: removed
    }

    [Fact]
    public void Merge_asks_when_the_master_copy_is_broken_and_the_source_copy_is_complete()
    {
        var (d, sp, _) = Host();
        using var _1 = d;
        using var _2 = sp;
        var broken = Mp4(1, tags: GoodTags);
        broken = [.. broken, .. new byte[100]];                                      // data after the last box: broken
        d.File(@"G\Videos\m.mp4", broken);
        d.File(@"G\In\s.mp4", Mp4(1, tags: GoodTags));
        var plan = sp.GetRequiredService<CleanupService>().Find(
            new CleanupOptions(P(d, "G", "In"), P(d, "G", "duplicates"), P(d, "G"), All), null, CancellationToken.None);
        Assert.Single(plan.Decisions);
        Assert.Empty(plan.Items);                                                    // nothing moves until the person decides
    }

    [Fact]
    public void Unsafe_targets_are_refused_and_changed_files_are_not_moved()
    {
        var (d, sp, _) = Host();
        using var _1 = d;
        using var _2 = sp;
        d.File(@"V\a.mp4", Mp4(1));
        d.File(@"V\b.mp4", Mp4(1));
        var clean = sp.GetRequiredService<CleanupService>();
        Assert.Throws<RecoveryServiceException>(() => clean.Find(new CleanupOptions(P(d, "V"), d.Path, null, All), null, CancellationToken.None));
        Assert.Throws<RecoveryServiceException>(() =>
            clean.Find(new CleanupOptions(P(d, "V"), Path.GetPathRoot(d.Path)!, null, All), null, CancellationToken.None));

        var plan = clean.Find(new CleanupOptions(P(d, "V"), P(d, "dup"), null, All), null, CancellationToken.None);
        var item = Assert.Single(plan.Items);
        File.SetLastWriteTimeUtc(item.SourcePath, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var r = clean.Execute(plan, plan.Items, null, CancellationToken.None);
        Assert.Equal(0, r.Moved);
        Assert.Equal(1, r.Skipped);
    }

    [Fact]
    public void Emptying_the_target_needs_both_steps_and_keeps_the_folder()
    {
        var (d, sp, confirmer) = Host();
        using var _1 = d;
        using var _2 = sp;
        d.File(@"dup\Exact copies\Videos\a.mp4", Mp4(1));
        d.File(@"dup\Broken\Photos\b.jpg", Jpeg(2, complete: false));
        var clean = sp.GetRequiredService<CleanupService>();
        var batch = clean.PrepareEmpty(P(d, "dup"));
        Assert.Equal(2, batch.Count);
        Assert.Equal(0, clean.Empty(P(d, "dup"), batch, "WRONG", null, CancellationToken.None).Moved);
        Assert.Equal(0, confirmer.Asked);
        Assert.True(File.Exists(P(d, @"dup\Exact copies\Videos\a.mp4")));

        var r = clean.Empty(P(d, "dup"), batch, batch.Code, null, CancellationToken.None);
        Assert.Equal(2, r.Moved);
        Assert.Equal(1, confirmer.Asked);
        Assert.Empty(Directory.GetFileSystemEntries(P(d, "dup")));
        Assert.True(Directory.Exists(P(d, "dup")));
    }
}
