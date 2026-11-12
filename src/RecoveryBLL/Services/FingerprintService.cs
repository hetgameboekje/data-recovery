using System.Diagnostics;
using RecoveryBLL.Dtos;
using RecoveryBLL.Fingerprinting;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;

namespace RecoveryBLL.Services;

/// <summary>
/// Steps 2 and 3 of cataloguing: fingerprint only what can be a duplicate, then read image headers.
///   - a file whose size no other catalogued file has can never be a duplicate: never read;
///   - files that share a size get a quick hash (first + last 64 KB);
///   - only files whose quick hash still matches get a full content hash.
/// Collisions are checked across ALL catalogued disks; files on unplugged disks wait until they are plugged in.
/// </summary>
public sealed class FingerprintService(
    DiskService disks,
    IFileRepository files,
    IFileContentReader reader,
    DictionaryService dictionaries,
    IKeepAwake keepAwake,
    IOperationLog log)
{
    const int FailingDiskTimeouts = 3;

    const int FlushEvery = 500;

    public IReadOnlyList<StageResult> Run(FingerprintOptions options, IProgress<ProgressInfo>? progress,
                                          CancellationToken ct, IReadOnlySet<long>? onlyDisks = null)
    {
        var roots = disks.GetMountedRoots().Where(kv => onlyDisks == null || onlyDisks.Contains(kv.Key)).ToDictionary();
        var results = new List<StageResult>();
        using var awake = keepAwake.Begin("Recovery Toolkit is fingerprinting files");
        if (roots.Count == 0)
        {
            log.Info("None of the catalogued disks are plugged in; nothing to fingerprint.");
            return results;
        }

        var threads = roots.ToDictionary(kv => kv.Key, kv => options.AutoThreads
            ? disks.RecommendedThreads(disks.GetDeviceForRoot(kv.Value))
            : options.ThreadsPerDisk);
        var imageExts = dictionaries.Current.ProbeableImageExtensions.ToList();

        results.Add(RunStage("Quick hash", FingerprintStage.QuickHash, roots, threads, options, imageExts, progress, ct,
            (job, s) => ContentHasher.Quick(job.Id, s, job.Size, options.Sha256), j => Math.Min(j.Size, 2L * ContentHasher.Chunk), sequential: false));
        if (ct.IsCancellationRequested) return results;

        results.Add(RunStage("Full hash", FingerprintStage.FullHash, roots, threads, options, imageExts, progress, ct,
            (job, s) => ContentHasher.Full(job.Id, s, job.Size, options.Sha256), j => j.Size, sequential: true));
        if (ct.IsCancellationRequested || !options.ReadImageInfo) return results;

        results.Add(RunStage("Image info", FingerprintStage.ImageInfo, roots, threads, options, imageExts, progress, ct,
            (job, s) =>
            {
                ImageHeaderReader.Info info;
                try { info = ImageHeaderReader.Read(s, job.Ext); }
                catch (Exception ex) when (ex is EndOfStreamException or ArgumentException) { info = new(null, null, null, null); }
                return new FingerprintResultDto
                {
                    Id = job.Id, Kind = FingerprintResultKind.Media,
                    Width = info.Width, Height = info.Height, Camera = info.Camera, Taken = info.Taken,
                };
            }, _ => 0, sequential: false));
        return results;
    }

    StageResult RunStage(string name, FingerprintStage stage, Dictionary<long, string> roots, Dictionary<long, int> threads,
                         FingerprintOptions options, IReadOnlyCollection<string> imageExts,
                         IProgress<ProgressInfo>? progress, CancellationToken ct,
                         Func<FileJobDto, Stream, FingerprintResultDto> work, Func<FileJobDto, long> plannedBytes, bool sequential)
    {
        var perDisk = new Dictionary<long, IReadOnlyList<FileJobDto>>();
        long total = 0, totalBytes = 0;
        foreach (var diskId in roots.Keys)
        {
            var jobs = RepositoryGuard.Run(() => files.GetJobs(diskId, stage, options.Sha256, imageExts), "Cannot read the catalog");
            if (jobs.Count == 0) continue;
            perDisk[diskId] = jobs;
            total += jobs.Count;
            totalBytes += jobs.Sum(plannedBytes);
        }
        if (total == 0)
        {
            log.Info($"{name}: nothing to do.");
            return new StageResult(name, 0, 0, 0, true);
        }
        log.Info($"{name}: {total:N0} files on {perDisk.Count} disk(s)" + (totalBytes > 0 ? $", {Format.Bytes(totalBytes)} to read" : ""));

        long done = 0, bytes = 0, errors = 0, lastReport = 0;
        var gone = new System.Collections.Concurrent.ConcurrentDictionary<long, byte>();      // unplugged or failing disks
        var timeouts = new System.Collections.Concurrent.ConcurrentDictionary<long, int>();
        var sw = Stopwatch.StartNew();
        var pending = new List<FingerprintResultDto>();
        var gate = new object();
        var saveGate = new object();

        void Flush(bool force)
        {
            List<FingerprintResultDto> todo;
            lock (gate)
            {
                if (!force && pending.Count < FlushEvery) return;
                todo = pending;
                pending = [];
            }
            if (todo.Count == 0) return;
            lock (saveGate) RepositoryGuard.Run(() => files.SaveResults(todo), "Cannot save fingerprints");
        }

        // Disks run in parallel (separate devices); within a disk, the configured number of readers.
        Parallel.ForEach(perDisk, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, perDisk.Count) }, kv =>
        {
            var root = roots[kv.Key];
            Parallel.ForEach(kv.Value, new ParallelOptions { MaxDegreeOfParallelism = threads[kv.Key] }, (job, state) =>
            {
                if (ct.IsCancellationRequested || gone.ContainsKey(kv.Key)) { state.Stop(); return; }
                var path = Path.Combine(root, job.Path);
                FingerprintResultDto r;
                long read = 0;
                try
                {
                    using var s = reader.OpenRead(path, sequential);
                    r = work(job, s);
                    read = plannedBytes(job);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Unplugged (or the letter went away): record NOTHING for this file. Otherwise every remaining file
                    // would be flagged missing or unreadable and skipped forever. Stop this disk; the rest continues.
                    if (!Directory.Exists(root))
                    {
                        if (gone.TryAdd(kv.Key, 0))
                            log.Warn($"{name}: {root} disappeared (unplugged?). Its files keep their state; plug it in and run 'hash' again.");
                        state.Stop();
                        return;
                    }
                    if (ex is FileNotFoundException or DirectoryNotFoundException)
                        r = new FingerprintResultDto { Id = job.Id, Kind = FingerprintResultKind.Missing };
                    else
                    {
                        Interlocked.Increment(ref errors);
                        log.Warn($"{name}: {path}: {ex.Message}");
                        r = new FingerprintResultDto { Id = job.Id, Kind = FingerprintResultKind.Error, Error = $"{name}: {ex.Message}" };
                        // A disk whose reads keep hanging is probably failing: stop hammering it.
                        if (ex is Exceptions.ReadTimeoutException && timeouts.AddOrUpdate(kv.Key, 1, (_, n) => n + 1) >= FailingDiskTimeouts)
                        {
                            if (gone.TryAdd(kv.Key, 1))
                                log.Warn($"{name}: {root} keeps timing out on reads. The disk may be failing: stop using it, " +
                                         "make a sector image first (old toolkit: Disk tools > Imaging), then catalog the image.");
                            state.Stop();
                        }
                    }
                }
                var d = Interlocked.Increment(ref done);
                var b = Interlocked.Add(ref bytes, read);
                lock (gate) pending.Add(r);
                Flush(false);
                var ms = sw.ElapsedMilliseconds;
                var last = Interlocked.Read(ref lastReport);
                if (ms - last >= 250 && Interlocked.CompareExchange(ref lastReport, ms, last) == last)
                    progress?.Report(new ProgressInfo(name, d, total, b, totalBytes, sw.Elapsed));
            });
        });
        Flush(true);
        progress?.Report(new ProgressInfo(name, done, total, bytes, totalBytes, sw.Elapsed));
        bool completed = !ct.IsCancellationRequested && gone.IsEmpty;
        if (!completed) log.Info($"{name}: stopped. Run it again to continue where it left off.");
        return new StageResult(name, done, bytes, errors, completed);
    }
}
