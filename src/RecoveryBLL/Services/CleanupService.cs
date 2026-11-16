using System.Diagnostics;
using System.IO.Hashing;
using System.Text.RegularExpressions;
using RecoveryBLL.Dtos;
using RecoveryBLL.Exceptions;
using RecoveryBLL.Fingerprinting;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;
using RecoveryBLL.Validation;

namespace RecoveryBLL.Services;

/// <summary>
/// Clean up: duplicates and junk on one page, as agreed with the user (docs/cleanup-session-2026-11-12.md).
///   Find    - reads the folders themselves (never the catalog: it can hold files that are gone) and decides per file.
///             Nothing is changed. The person sees the exact list: what stays, what goes where, and why.
///   Execute - only after the person's OK. Re-checks every file right before moving it; never overwrites (" (2)");
///             never deletes; writes an undo record; keeps the catalog in step.
///   Undo    - moves the files of the last run back to where they were.
///   Empty   - deleting is a separate, later step: the target folder is emptied only after both deletion steps.
/// Rules: the target only ever receives unwanted files. In merge mode, what is already in a master folder always stays.
/// A copy that stays: complete > tags damaged > unknown > broken, then most tags, then the lowest number in the name.
/// </summary>
public sealed class CleanupService(
    DictionaryService dictionaries,
    DiskService disks,
    IFileSystemWalker walker,
    IFileContentReader reader,
    IFileStore store,
    ISourceFileOperations files,
    IProtectedLocations protectedLocations,
    RunStateService runState,
    ICatalogSync catalog,
    ICleanupLogStore logs,
    IDeletionConfirmer confirmer,
    IKeepAwake keepAwake,
    IOperationLog log)
{
    const string AppsAndData = "Apps and data";

    sealed class F
    {
        public required string Path;
        public required string Name;
        public required string Ext;
        public required string Type;
        public long Size;
        public long Mtime;
        public bool InMaster;
        public string Rel = "";                 // relative to the source (source files only)
        public VideoStatus Status = VideoStatus.Unknown;
        public IReadOnlyList<Mp4Structure.Box>? Boxes;
        public long Tags;
        public bool Protected;                  // inside a keep-together folder (a world, a code project): never split
        public string? BundleRoot;              // inside a reinstallable app folder: moves as a whole
        public CleanupCategory? Category;
        public string? Reason;
        public string? Keep;
        public bool Undecided;                  // part of a case the person decides: nothing in it moves
    }

    static readonly HashSet<string> Mp4Like = new(StringComparer.OrdinalIgnoreCase) { "mp4", "mov", "m4v", "3gp", "3g2", "qt" };

    // ------------------------------------------------------------------ Find (read-only)

    public CleanupPlan Find(CleanupOptions options, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var (source, target, master) = CheckPaths(options);
        var d = dictionaries.Current;
        List<string> masterFolders = master == null ? [] : d.CategoryOrder.Append(d.DefaultCategory).Distinct()
            .Select(c => System.IO.Path.Combine(master, c)).Where(Directory.Exists).ToList();
        var sw = Stopwatch.StartNew();
        var all = new List<F>();
        long unreadable = 0;

        void Walk(string folder, bool inMaster)
        {
            var skip = new List<string> { target };
            skip.AddRange(masterFolders.Where(m => !inMaster));
            foreach (var w in walker.Walk(folder, d.SkipFolders, skip, (_, _) => unreadable++, ct))
            {
                var ext = System.IO.Path.GetExtension(w.Name).TrimStart('.').ToLowerInvariant();
                all.Add(new F
                {
                    Path = w.FullPath, Name = w.Name, Ext = ext, Type = d.CategoryOf(ext), Size = w.Size, Mtime = w.MtimeTicks,
                    InMaster = inMaster, Rel = inMaster ? "" : System.IO.Path.GetRelativePath(folder, w.FullPath),
                });
                if (all.Count % 2000 == 0) progress?.Report(new ProgressInfo("Listing files", all.Count, 0, 0, 0, sw.Elapsed));
            }
        }
        Walk(source, false);
        foreach (var m in masterFolders) Walk(m, true);
        ct.ThrowIfCancellationRequested();
        var src = all.Where(f => !f.InMaster).ToList();
        log.Info($"Clean up: checking {src.Count:N0} files in {source}" + (master != null ? $" against {masterFolders.Count} master folders in {master}" : ""));

        // Video structure: needed for broken, same video and which copy stays.
        foreach (var f in all.Where(f => f.Type == "Videos" || Mp4Like.Contains(f.Ext)))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var s = reader.OpenRead(f.Path, sequential: false);
                f.Boxes = Mp4Structure.Boxes(s);
                f.Status = Mp4Structure.Status(s);
                f.Tags = Mp4Structure.TagBytes(f.Boxes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { unreadable++; }
        }

        var on = options.Categories;
        TagJunk(src, d, on, progress, sw, ct, ref unreadable);
        var decisions = new List<CleanupDecision>();
        if (on.Contains(CleanupCategory.ExactCopy)) FindExactCopies(all, progress, sw, ct, ref unreadable);
        if (on.Contains(CleanupCategory.SameVideo)) FindSameVideos(all, decisions, progress, sw, ct, ref unreadable);

        foreach (var f in src.Where(f => f.Category == null && !f.Protected && f.BundleRoot == null))
        {
            ct.ThrowIfCancellationRequested();
            if (on.Contains(CleanupCategory.Broken) && BrokenReason(f, ref unreadable) is { } why)
                (f.Category, f.Reason) = (CleanupCategory.Broken, why);
            else if (on.Contains(CleanupCategory.SmallFile) && IsSmall(f))
                (f.Category, f.Reason) = (CleanupCategory.SmallFile, $"only {f.Size:N0} bytes and not a complete file");
        }

        // The plan: unwanted files to the target, and in merge mode the wanted ones to the master folders.
        var items = new List<CleanupItem>();
        foreach (var f in src)
        {
            if (f.Category is { } c)
            {
                var dest = c == CleanupCategory.AppFolders
                    ? System.IO.Path.Combine(target, c.FolderName(), f.Rel)
                    : System.IO.Path.Combine(target, c.FolderName(), f.Type, f.Name);
                items.Add(new CleanupItem(CleanupAction.ToTarget, c, f.Type, f.Path, dest, f.Size, f.Mtime, f.Reason ?? c.Explanation(), f.Keep));
            }
            else if (master != null && !f.Undecided)
            {
                var dest = f.Protected
                    ? System.IO.Path.Combine(master, AppsAndData, f.Rel)          // keep-together folders stay whole
                    : System.IO.Path.Combine(master, f.Type, f.Name);
                items.Add(new CleanupItem(CleanupAction.ToMaster, null, f.Type, f.Path, dest, f.Size, f.Mtime,
                    f.Protected ? "kept whole (belongs together)" : "not in the master yet", null));
            }
        }
        progress?.Report(new ProgressInfo("Done", src.Count, src.Count, 0, 0, sw.Elapsed));
        return new CleanupPlan
        {
            Options = options, Source = source, Target = target, MasterRoot = master, Items = items, Decisions = decisions,
            FilesChecked = src.Count, Unreadable = unreadable,
        };
    }

    (string Source, string Target, string? Master) CheckPaths(CleanupOptions o)
    {
        var source = PathSafetyValidator.Validate(o.Source, PathUse.ScanSource, protectedLocations);
        if (!Directory.Exists(source)) throw new RecoveryServiceException("Folder not found: " + source);
        var target = PathSafetyValidator.Validate(o.Target, PathUse.Destination, protectedLocations);
        if (IsDriveRoot(target)) throw new RecoveryServiceException("Choose a folder for the unwanted files (e.g. G:\\duplicates), not a whole drive.");
        if (Same(target, source) || Inside(source, target))
            throw new RecoveryServiceException($"{target} contains the folder being cleaned. Choose a separate folder for the unwanted files.");
        string? master = null;
        if (!string.IsNullOrWhiteSpace(o.MasterRoot))
        {
            master = PathSafetyValidator.Validate(o.MasterRoot, PathUse.Destination, protectedLocations);
            if (Same(master, source) || Inside(master, source))
                throw new RecoveryServiceException("The master location cannot be inside the folder being merged.");
            foreach (var c in dictionaries.Current.CategoryOrder.Append(dictionaries.Current.DefaultCategory))
            {
                var mf = System.IO.Path.Combine(master, c);
                if (Same(mf, source) || Inside(source, mf))
                    throw new RecoveryServiceException($"{source} is (inside) the master folder {mf}. To clean a master folder, leave the master empty.");
                if (Same(mf, target) || Inside(target, mf))
                    throw new RecoveryServiceException($"The unwanted files cannot go into the master folder {mf}.");
            }
        }
        if (target.Length > 0 && !string.Equals(System.IO.Path.GetPathRoot(target), System.IO.Path.GetPathRoot(source), StringComparison.OrdinalIgnoreCase))
            log.Info($"{target} is on another drive than {source}: files are copied, checked, and only then removed from the source.");
        return (source, target, master);
    }

    void TagJunk(List<F> src, RecoveryDictionaries d, IReadOnlySet<CleanupCategory> on, IProgress<ProgressInfo>? progress,
                 Stopwatch sw, CancellationToken ct, ref long unreadable)
    {
        var facts = new List<FileFactsDto>(src.Count);
        bool needSizes = on.Contains(CleanupCategory.ThumbnailsAndIcons) || on.Contains(CleanupCategory.ImageSets);
        for (int i = 0; i < src.Count; i++)
        {
            var f = src[i];
            int? w = null, h = null;
            bool camera = false;
            if (needSizes && d.ProbeableImageExtensions.Contains(f.Ext))
            {
                try
                {
                    using var s = reader.OpenRead(f.Path, sequential: false);
                    var info = ImageHeaderReader.Read(s, f.Ext);
                    (w, h, camera) = (info.Width, info.Height, info.Camera != null);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { unreadable++; }
                if (i % 2000 == 0) progress?.Report(new ProgressInfo("Reading picture sizes", i, src.Count, 0, 0, sw.Elapsed));
            }
            facts.Add(new FileFactsDto(i, 0, f.Rel, f.Ext, f.Size, f.Type, w, h, camera));
        }
        ct.ThrowIfCancellationRequested();
        var tags = FileTagger.Tag(facts, d.Junk, d.KeepTogether, junk: true).Tags;
        foreach (var (id, tag) in tags)
        {
            var f = src[(int)id];
            switch (tag.Kind)
            {
                case TagKind.KeepTogether:
                    f.Protected = true;                                   // a world, a database, a code project: never judged
                    break;
                case TagKind.ReviewTogether:
                    f.BundleRoot = tag.BundleRoot;                        // never split; moves only as a whole
                    if (on.Contains(CleanupCategory.AppFolders))
                        (f.Category, f.Reason) = (CleanupCategory.AppFolders, $"{tag.Reason}: {tag.BundleRoot}");
                    break;
                case TagKind.Junk:
                    var c = tag.Reason switch
                    {
                        FileTagger.SystemFiles or FileTagger.Caches => CleanupCategory.CachesAndSystem,
                        FileTagger.IconsAndSkins or FileTagger.Thumbnails => CleanupCategory.ThumbnailsAndIcons,
                        FileTagger.ImageSets => CleanupCategory.ImageSets,
                        _ => (CleanupCategory?)null,                      // "Scraps": the small-file rule decides (it checks structure)
                    };
                    if (c is { } cat && on.Contains(cat)) (f.Category, f.Reason) = (cat, tag.Reason.ToLowerInvariant());
                    break;
            }
        }
    }

    static int StatusRank(VideoStatus s) => s switch
    {
        VideoStatus.Complete => 0, VideoStatus.TagsDamaged => 1, VideoStatus.Unknown => 2, _ => 3,
    };

    static (long, string) NameNumber(string name)
    {
        var m = Regex.Match(name, @"\d+");
        return (m.Success && long.TryParse(m.Value, out var n) ? n : long.MaxValue, name.ToLowerInvariant());
    }

    /// <summary>
    /// The copy that stays: a master copy when there is one, else complete > ... > broken, most tags, lowest number in the
    /// name, then the shortest name ("photo.jpg" before "photo copy.jpg" / "photo (2).jpg"), then alphabetical.
    /// </summary>
    static F Keeper(IEnumerable<F> group) => group
        .OrderBy(f => f.InMaster ? 0 : 1).ThenBy(f => StatusRank(f.Status)).ThenByDescending(f => f.Tags)
        .ThenBy(f => NameNumber(f.Name).Item1).ThenBy(f => f.Name.Length).ThenBy(f => NameNumber(f.Name).Item2)
        .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
        .First();

    static bool Judgeable(F f) => f.InMaster || (f.Category == null && !f.Protected && f.BundleRoot == null);

    void FindExactCopies(List<F> all, IProgress<ProgressInfo>? progress, Stopwatch sw, CancellationToken ct, ref long unreadable)
    {
        var groups = all.Where(f => f.Size > 0 && Judgeable(f)).GroupBy(f => f.Size)
            .Where(g => g.Count() > 1 && g.Any(f => !f.InMaster)).ToList();
        long total = groups.Sum(g => g.Sum(f => f.Size)), done = 0;
        var byHash = new Dictionary<(long, string), List<F>>();
        foreach (var f in groups.SelectMany(g => g))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var s = reader.OpenRead(f.Path, sequential: true);
                var hash = Convert.ToHexString(ContentHasher.Full(0, s, f.Size, false).FullHash!);
                var key = (f.Size, hash);
                if (!byHash.TryGetValue(key, out var list)) byHash[key] = list = [];
                list.Add(f);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { unreadable++; }
            done += f.Size;
            progress?.Report(new ProgressInfo("Comparing (exact copies)", 0, 0, done, total, sw.Elapsed));
        }
        foreach (var g in byHash.Values.Where(g => g.Count > 1))
        {
            var keep = Keeper(g);
            foreach (var f in g.Where(f => f != keep && !f.InMaster))
                (f.Category, f.Reason, f.Keep) = (CleanupCategory.ExactCopy, $"same as {keep.Name}", keep.Path);
        }
    }

    void FindSameVideos(List<F> all, List<CleanupDecision> decisions, IProgress<ProgressInfo>? progress, Stopwatch sw,
                        CancellationToken ct, ref long unreadable)
    {
        var groups = all.Where(f => Judgeable(f) && Mp4Structure.MediaShape(f.Boxes) != null)
            .GroupBy(f => Mp4Structure.MediaShape(f.Boxes)!)
            .Where(g => g.Count() > 1 && g.Any(f => !f.InMaster)).ToList();
        long total = groups.Sum(g => g.Sum(f => f.Size)), done = 0;
        var byHash = new Dictionary<string, List<F>>();
        foreach (var f in groups.SelectMany(g => g))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var s = reader.OpenRead(f.Path, sequential: true);
                var key = Convert.ToHexString(Mp4Structure.MediaHash(s, f.Boxes!, ct));
                if (!byHash.TryGetValue(key, out var list)) byHash[key] = list = [];
                list.Add(f);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { unreadable++; }
            done += f.Size;
            progress?.Report(new ProgressInfo("Comparing (same video)", 0, 0, done, total, sw.Elapsed));
        }
        foreach (var g in byHash.Values.Where(g => g.Count > 1))
        {
            var keep = Keeper(g);
            var bestSource = g.Where(f => !f.InMaster).OrderBy(f => StatusRank(f.Status)).First();
            if (keep.InMaster && keep.Status == VideoStatus.Broken && bestSource.Status is VideoStatus.Complete or VideoStatus.TagsDamaged)
            {
                // The master's copy is broken and the source has a good one: the person decides, nothing moves.
                decisions.Add(new CleanupDecision(bestSource.Path, keep.Path,
                    "The copy in the master is broken; the copy being merged is complete. Which one should stay?"));
                foreach (var f in g.Where(f => !f.InMaster)) f.Undecided = true;
                continue;
            }
            foreach (var f in g.Where(f => f != keep && !f.InMaster && f.Category == null))
                (f.Category, f.Reason, f.Keep) = (CleanupCategory.SameVideo, $"same video as {keep.Name}: {Difference(f, keep)}", keep.Path);
        }
    }

    static string Difference(F f, F keep) =>
        f.Status == VideoStatus.Broken ? "this copy is broken"
        : f.Status == VideoStatus.TagsDamaged && keep.Status == VideoStatus.Complete ? "this copy has damaged tags"
        : f.Tags < keep.Tags ? (f.Tags == 0 ? "this copy has no tags" : "this copy has fewer tags")
        : "other tags";

    string? BrokenReason(F f, ref long unreadable)
    {
        if (f.Type == "Videos" || Mp4Like.Contains(f.Ext))
            return f.Status == VideoStatus.Broken ? "picture data or index missing, or cut off" : null;
        if (f.Type != "Photos") return null;
        try
        {
            using var s = reader.OpenRead(f.Path, sequential: false);
            return ImageIntegrity.Problem(s, f.Ext);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { unreadable++; return null; }
    }

    /// <summary>LESSON 2026-11-12: size alone is wrong; a complete 53 KB clip is a real video.</summary>
    static bool IsSmall(F f) =>
        f.Type == "Videos" ? f.Size < CleanupOptions.SmallVideo && f.Status is not (VideoStatus.Complete or VideoStatus.TagsDamaged)
        : f.Type == "Photos" && f.Size < CleanupOptions.SmallPhoto;

    // ------------------------------------------------------------------ Execute (after the person's OK)

    public CleanupRunResult Execute(CleanupPlan plan, IReadOnlyCollection<CleanupItem> items, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var held = new List<string>();
        try
        {
            foreach (var place in new[] { plan.Target, plan.MasterRoot }.OfType<string>())
            {
                runState.Hold(LeaseKind.CopyDestination, place);
                held.Add(place);
            }
            using var awake = keepAwake.Begin("Recovery Toolkit is cleaning up");
            var ordered = items.OrderBy(i => i.Action == CleanupAction.ToTarget ? 0 : 1).ToList();
            var result = Move(ordered.Select(i => (i.SourcePath, i.DestPath, i.Size, (long?)i.MtimeTicks, i.Keep)).ToList(),
                "Clean up", progress, ct, out var entries);
            if (plan.MasterRoot != null && result.Completed) RemoveEmptyFolders(plan.Source, includeRoot: true);
            else RemoveEmptyFolders(entries.Select(e => System.IO.Path.GetDirectoryName(e.From)!), plan.Source);
            return result with { LogFile = entries.Count > 0 ? logs.Save(entries) : null };
        }
        finally
        {
            foreach (var h in held) runState.Release(LeaseKind.CopyDestination, h);
        }
    }

    /// <summary>The last run that can be undone: (when, how many files), or null.</summary>
    public (string File, int Files)? LastRun() => logs.Latest() is { } l ? (l.File, l.Entries.Count) : null;

    public CleanupRunResult UndoLast(IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var last = logs.Latest() ?? throw new RecoveryServiceException("There is nothing to undo.");
        using var awake = keepAwake.Begin("Recovery Toolkit is undoing a clean up");
        var back = last.Entries.Reverse().Select(e => (e.To, e.From, e.Size, (long?)null, (string?)null)).ToList();
        var result = Move(back, "Undo", progress, ct, out _, exactTarget: true);   // folders are left as they are
        if (result.Completed && result.Errors == 0) logs.MarkUndone(last.File);
        return result with { LogFile = last.File };
    }

    CleanupRunResult Move(IReadOnlyList<(string From, string To, long Size, long? Mtime, string? Keep)> moves, string what,
                       IProgress<ProgressInfo>? progress, CancellationToken ct, out List<CleanupLogEntry> entries, bool exactTarget = false)
    {
        entries = [];
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mounted = disks.GetMountedRoots();
        long moved = 0, movedBytes = 0, skipped = 0, errors = 0, done = 0, total = moves.Sum(m => m.Size), doneBytes = 0;
        var sw = Stopwatch.StartNew();
        log.Info($"{what}: moving {moves.Count:N0} files ({Format.Bytes(total)})");
        foreach (var m in moves)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (!store.Exists(m.From)) { Skip($"{m.From} is not there any more."); continue; }
                if (store.Length(m.From) != m.Size || (m.Mtime is { } t && store.LastWriteTicks(m.From) != t))
                {
                    Skip($"{m.From} changed since the list was made; not moved."); continue;
                }
                if (m.Keep != null && !store.Exists(m.Keep)) { Skip($"{m.From}: the copy that stays ({m.Keep}) is gone; not moved."); continue; }
                string dest;
                if (exactTarget)
                {
                    if (store.Exists(m.To)) { Skip($"{m.To} is taken by another file; {m.From} stays where it is."); continue; }
                    dest = m.To;
                }
                else dest = FreeName(m.To, taken);
                taken.Add(dest);
                if (SameVolume(m.From, dest)) files.Rename(m.From, dest);
                else CopyThenRemove(m.From, dest, m.Size, ct);
                SyncCatalog(mounted, m.From, dest);
                entries.Add(new CleanupLogEntry(m.From, dest, m.Size));
                moved++;
                movedBytes += m.Size;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or RecoveryServiceException)
            {
                errors++;
                log.Warn($"{what}: {m.From}: {ex.Message}");
            }
            finally
            {
                done++;
                doneBytes += m.Size;
                if (done % 50 == 0 || done == moves.Count)
                    progress?.Report(new ProgressInfo(what, done, moves.Count, doneBytes, total, sw.Elapsed));
            }
        }
        bool completed = !ct.IsCancellationRequested;
        log.Info($"{what}: moved {moved:N0} files ({Format.Bytes(movedBytes)})" + (skipped > 0 ? $", {skipped:N0} skipped" : "")
                 + (errors > 0 ? $", {errors:N0} errors" : "") + (completed ? "" : " - STOPPED"));
        return new CleanupRunResult(moved, movedBytes, skipped, errors, completed, null);

        void Skip(string why) { skipped++; log.Warn(why); }
    }

    void CopyThenRemove(string from, string to, long size, CancellationToken ct)
    {
        var temp = to + ".rtpart";
        try
        {
            byte[] hash;
            using (var s = reader.OpenRead(from, sequential: true))
            using (var d = store.CreateTemp(temp))
            {
                var x = new XxHash128();
                var buf = new byte[1024 * 1024];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    x.Append(buf.AsSpan(0, n));
                    d.Write(buf, 0, n);
                }
                if (d is FileStream fs) fs.Flush(flushToDisk: true);
                hash = x.GetCurrentHash();
            }
            using (var check = reader.OpenRead(temp, sequential: true))
                if (!ContentHasher.Full(0, check, size, false).FullHash!.AsSpan().SequenceEqual(hash))
                    throw new IOException("the copy reads back differently; the original was kept");
            store.Commit(temp, to, store.LastWriteTicks(from));
        }
        catch
        {
            try { if (store.Exists(temp)) store.Delete(temp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
        files.Delete(from);   // only after the copy is complete, checked and in place
    }

    void SyncCatalog(IReadOnlyDictionary<long, string> mounted, string from, string to)
    {
        var (fromDisk, fromRel) = DiskOf(mounted, from);
        if (fromDisk == null) return;
        var (toDisk, toRel) = DiskOf(mounted, to);
        RepositoryGuard.Run(() =>
        {
            if (toDisk != null) catalog.RecordMove(fromDisk.Value, fromRel, toDisk.Value, toRel, store.LastWriteTicks(to), RepositoryGuard.Now());
            else catalog.MarkMissing(fromDisk.Value, fromRel);
        }, "Cannot update the catalog");
    }

    static (long?, string) DiskOf(IReadOnlyDictionary<long, string> mounted, string path)
    {
        foreach (var (id, root) in mounted.OrderByDescending(kv => kv.Value.Length))
        {
            var r = root.EndsWith('\\') ? root : root + "\\";
            if (path.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return (id, path[r.Length..]);
        }
        return (null, path);
    }

    string FreeName(string path, HashSet<string> taken)
    {
        bool Free(string p) => !store.Exists(p) && !store.Exists(p + ".rtpart") && !taken.Contains(p);
        if (Free(path)) return path;
        var dir = System.IO.Path.GetDirectoryName(path) ?? "";
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var ext = System.IO.Path.GetExtension(path);
        for (int n = 2; ; n++)
        {
            var p = System.IO.Path.Combine(dir, $"{name} ({n}){ext}");
            if (Free(p)) return p;
        }
    }

    /// <summary>Removes folders that are now completely empty: under the given folders, up to (not including) <paramref name="stopAt"/>.</summary>
    void RemoveEmptyFolders(IEnumerable<string> folders, string? stopAt)
    {
        foreach (var start in folders.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(f => f.Length))
            for (var dir = start; dir != null && !IsDriveRoot(dir) && (stopAt == null || Inside(dir, stopAt)); dir = System.IO.Path.GetDirectoryName(dir))
                if (!files.DeleteFolderIfEmpty(dir)) break;
    }

    void RemoveEmptyFolders(string root, bool includeRoot)
    {
        if (!Directory.Exists(root)) return;
        foreach (var dir in Directory.EnumerateDirectories(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
                     .OrderByDescending(p => p.Length).ToList())
            files.DeleteFolderIfEmpty(dir);
        if (includeRoot && !IsDriveRoot(root)) files.DeleteFolderIfEmpty(root);
    }

    // ------------------------------------------------------------------ Empty the target (delete, both steps)

    /// <summary>Step 1 of deleting: the exact list of everything in the target folder, with its approval code.</summary>
    public DeletionBatch PrepareEmpty(string target)
    {
        var t = PathSafetyValidator.Validate(target, PathUse.Destination, protectedLocations).TrimEnd('\\');
        if (IsDriveRoot(t)) throw new RecoveryServiceException("A whole drive cannot be emptied here.");
        if (!Directory.Exists(t)) throw new RecoveryServiceException("Folder not found: " + t);
        var list = walker.Walk(t, new HashSet<string>(), [], (_, _) => { }, CancellationToken.None)
            .Select(f => new DeletionCandidate(f.FullPath, f.Size, "in the clean-up folder " + t)).ToList();
        return new DeletionBatch(list);
    }

    /// <summary>Deletes exactly the approved list after the final confirmation (step 2); each file is re-checked first.</summary>
    public CleanupRunResult Empty(string target, DeletionBatch batch, string approvedCode, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var t = PathSafetyValidator.Validate(target, PathUse.Destination, protectedLocations).TrimEnd('\\');
        if (!batch.IsApprovedBy(approvedCode) || !confirmer.ConfirmFinal(batch))
        {
            log.Info("Emptying the clean-up folder was not confirmed: nothing was deleted.");
            return new CleanupRunResult(0, 0, 0, 0, false, null);
        }
        var mounted = disks.GetMountedRoots();
        long deleted = 0, bytes = 0, skipped = 0, errors = 0, done = 0;
        var sw = Stopwatch.StartNew();
        foreach (var f in batch.Files)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (!Inside(f.Path, t) || !store.Exists(f.Path) || store.Length(f.Path) != f.Size) { skipped++; continue; }
                files.Delete(f.Path);
                var (disk, rel) = DiskOf(mounted, f.Path);
                if (disk != null) RepositoryGuard.Run(() => catalog.MarkMissing(disk.Value, rel), "Cannot update the catalog");
                deleted++;
                bytes += f.Size;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors++; log.Warn($"Cannot delete {f.Path}: {ex.Message}"); }
            if (++done % 200 == 0) progress?.Report(new ProgressInfo("Deleting", done, batch.Count, 0, 0, sw.Elapsed));
        }
        RemoveEmptyFolders(t, includeRoot: false);
        log.Info($"Emptied {t}: deleted {deleted:N0} files ({Format.Bytes(bytes)})" + (skipped > 0 ? $", {skipped:N0} skipped (changed)" : ""));
        return new CleanupRunResult(deleted, bytes, skipped, errors, !ct.IsCancellationRequested, null);
    }

    // ------------------------------------------------------------------ path helpers

    static bool IsDriveRoot(string p) => string.Equals(System.IO.Path.GetPathRoot(p)?.TrimEnd('\\'), p.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    static bool Same(string a, string b) => string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    static bool Inside(string path, string folder) => path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    static bool SameVolume(string a, string b) =>
        string.Equals(System.IO.Path.GetPathRoot(a), System.IO.Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);
}
