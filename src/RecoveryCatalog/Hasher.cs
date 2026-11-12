using System.IO.Hashing;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace RecoveryCatalog;

/// <summary>
/// Step 2 + 3: fingerprint only what can be a duplicate, then read image headers.
///   - a file with a size no other file has can never be a duplicate: not read at all;
///   - files sharing a size get a quick hash (first + last 64 KB);
///   - only files that still match get a full content hash.
/// Size collisions are checked across every disk in the catalog, so scanning a new disk
/// can make files on an earlier disk need hashing; that happens whenever that disk is plugged in.
/// </summary>
static class Hasher
{
    public const int Chunk = 64 * 1024;

    public sealed record Options(int ThreadsPerDisk, bool Sha256, bool Media);

    sealed record Job(long Id, string Path, long Size, string Ext);

    public static void Run(Catalog cat, Options o, CancellationToken ct, IReadOnlySet<long>? onlyDisks = null)
    {
        var roots = cat.MountedRoots();
        if (onlyDisks != null) roots = roots.Where(kv => onlyDisks.Contains(kv.Key)).ToDictionary();
        if (roots.Count == 0)
        {
            Log.Info("None of the catalogued disks are plugged in; nothing to fingerprint.");
            return;
        }

        Stage(cat, roots, o, ct, "Quick hash", """
            SELECT f.id, f.path, f.size, f.ext FROM files f
            WHERE f.disk_id = $d AND f.missing = 0 AND f.size > 0 AND f.quick_hash IS NULL AND f.error IS NULL
              AND EXISTS (SELECT 1 FROM files g WHERE g.size = f.size AND g.id <> f.id AND g.missing = 0)
            ORDER BY f.path
            """, QuickHash);
        if (ct.IsCancellationRequested) return;

        var fullFilter = o.Sha256 ? "(f.full_hash IS NULL OR f.sha256 IS NULL)" : "f.full_hash IS NULL";
        Stage(cat, roots, o, ct, "Full hash", $"""
            SELECT f.id, f.path, f.size, f.ext FROM files f
            WHERE f.disk_id = $d AND f.missing = 0 AND f.error IS NULL AND f.quick_hash IS NOT NULL AND {fullFilter}
              AND EXISTS (SELECT 1 FROM files g WHERE g.size = f.size AND g.quick_hash = f.quick_hash AND g.id <> f.id AND g.missing = 0)
            ORDER BY f.path
            """, FullHash);
        if (ct.IsCancellationRequested || !o.Media) return;

        var exts = string.Join(",", Categories.ProbeableImages.Select(e => $"'{e}'"));
        Stage(cat, roots, o, ct, "Image info", $"""
            SELECT f.id, f.path, f.size, f.ext FROM files f
            WHERE f.disk_id = $d AND f.missing = 0 AND f.error IS NULL AND f.media_done = 0 AND f.ext IN ({exts})
            ORDER BY f.path
            """, Probe);
    }

    // A worker returns the SQL update to apply for one file; updates are written in batches.
    delegate Update Work(Job job, string fullPath, bool sha256);

    sealed record Update(long Id, long BytesRead, string Sql, (string, object?)[] Params);

    static void Stage(Catalog cat, Dictionary<long, string> roots, Options o, CancellationToken ct,
                      string name, string sql, Work work)
    {
        var perDisk = new Dictionary<long, List<Job>>();
        long total = 0, totalBytes = 0;
        foreach (var (diskId, _) in roots)
        {
            var jobs = new List<Job>();
            using (var c = cat.Cmd(sql, null, ("$d", diskId)))
            using (var r = c.ExecuteReader())
                while (r.Read()) jobs.Add(new(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.IsDBNull(3) ? "" : r.GetString(3)));
            if (jobs.Count == 0) continue;
            perDisk[diskId] = jobs;
            total += jobs.Count;
            totalBytes += name == "Quick hash" ? jobs.Sum(j => Math.Min(j.Size, 2L * Chunk))
                        : name == "Full hash" ? jobs.Sum(j => j.Size) : 0;
        }
        if (total == 0) { Log.Info($"{name}: nothing to do."); return; }
        Log.Info($"{name}: {total:N0} files on {perDisk.Count} disk(s)" + (totalBytes > 0 ? $", {Fmt.Bytes(totalBytes)} to read" : ""));

        var progress = new Progress(name, total, totalBytes);
        var pending = new List<Update>();
        var gate = new object();
        void Flush(bool force)
        {
            List<Update> todo;
            lock (gate)
            {
                if (!force && pending.Count < 500) return;
                todo = pending;
                pending = new List<Update>();
            }
            if (todo.Count == 0) return;
            lock (cat.Lock)
            {
                using var tx = cat.Db.BeginTransaction();
                foreach (var u in todo)
                {
                    using var c = cat.Cmd(u.Sql, tx, [.. u.Params, ("$id", u.Id)]);
                    c.ExecuteNonQuery();
                }
                tx.Commit();
            }
        }

        // Disks run in parallel (each is its own device); within a disk, ThreadsPerDisk readers.
        // One reader per HDD is fastest: parallel reads on a spinning disk just make it seek.
        Parallel.ForEach(perDisk, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, perDisk.Count) }, kv =>
        {
            var root = roots[kv.Key];
            Parallel.ForEach(kv.Value, new ParallelOptions { MaxDegreeOfParallelism = o.ThreadsPerDisk, CancellationToken = CancellationToken.None }, (job, state) =>
            {
                if (ct.IsCancellationRequested) { state.Stop(); return; }
                var full = Path.Combine(root, job.Path);
                Update u;
                try
                {
                    u = work(job, full, o.Sha256);
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    u = new(job.Id, 0, "UPDATE files SET missing = 1 WHERE id = $id", []);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ChangedException)
                {
                    Log.Warn($"{name}: {full}: {ex.Message}");
                    u = new(job.Id, 0, "UPDATE files SET error = $e WHERE id = $id", [("$e", $"{name}: {ex.Message}")]);
                }
                progress.Add(u.BytesRead);
                lock (gate) pending.Add(u);
                Flush(false);
            });
        });
        Flush(true);
        progress.Done();
        if (ct.IsCancellationRequested) Log.Info($"{name}: stopped. Run the same command again to continue where it left off.");
    }

    sealed class ChangedException(string msg) : Exception(msg);

    static void ReadFully(SafeFileHandle h, Span<byte> buf, long offset)
    {
        int got = 0;
        while (got < buf.Length)
        {
            int n = RandomAccess.Read(h, buf[got..], offset + got);
            if (n == 0) throw new ChangedException("file is shorter than when it was scanned (run scan again)");
            got += n;
        }
    }

    static Update QuickHash(Job job, string path, bool sha256)
    {
        using var h = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess);
        long len = RandomAccess.GetLength(h);
        if (len != job.Size) throw new ChangedException($"size changed since scan ({job.Size:N0} -> {len:N0} bytes, run scan again)");

        var x = new XxHash128();
        if (job.Size <= 2L * Chunk)
        {
            // Small file: the quick read already covers everything, so this is also the full hash.
            var all = new byte[job.Size];
            ReadFully(h, all, 0);
            x.Append(all);
            var hash = x.GetCurrentHash();
            var ps = new List<(string, object?)> { ("$q", hash), ("$f", hash) };
            var extra = "";
            if (sha256) { ps.Add(("$s", SHA256.HashData(all))); extra = ", sha256 = $s"; }
            return new(job.Id, job.Size, $"UPDATE files SET quick_hash = $q, full_hash = $f{extra} WHERE id = $id", [.. ps]);
        }

        var buf = new byte[Chunk];
        ReadFully(h, buf, 0);
        x.Append(buf);
        ReadFully(h, buf, job.Size - Chunk);
        x.Append(buf);
        x.Append(BitConverter.GetBytes(job.Size));
        return new(job.Id, 2L * Chunk, "UPDATE files SET quick_hash = $q WHERE id = $id", [("$q", x.GetCurrentHash())]);
    }

    static Update FullHash(Job job, string path, bool sha256)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
        if (fs.Length != job.Size) throw new ChangedException($"size changed since scan ({job.Size:N0} -> {fs.Length:N0} bytes, run scan again)");
        var x = new XxHash128();
        using var s = sha256 ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
        var buf = new byte[1024 * 1024];
        long total = 0;
        int n;
        while ((n = fs.Read(buf, 0, buf.Length)) > 0)
        {
            x.Append(buf.AsSpan(0, n));
            s?.AppendData(buf, 0, n);
            total += n;
        }
        if (total != job.Size) throw new ChangedException("file changed while reading (run scan again)");
        return s == null
            ? new(job.Id, total, "UPDATE files SET full_hash = $f WHERE id = $id", [("$f", x.GetCurrentHash())])
            : new(job.Id, total, "UPDATE files SET full_hash = $f, sha256 = $s WHERE id = $id", [("$f", x.GetCurrentHash()), ("$s", s.GetHashAndReset())]);
    }

    static Update Probe(Job job, string path, bool _)
    {
        MediaProbe.Info info;
        try { info = MediaProbe.Read(path, job.Ext); }
        catch (Exception ex) when (ex is EndOfStreamException or ArgumentException) { info = new(null, null, null, null); }
        return new(job.Id, 0, "UPDATE files SET width = $w, height = $h, camera = $c, taken = $t, media_done = 1 WHERE id = $id",
            [("$w", info.Width), ("$h", info.Height), ("$c", info.Camera), ("$t", info.Taken)]);
    }
}
