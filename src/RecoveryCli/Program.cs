using Microsoft.Extensions.DependencyInjection;
using RecoveryBLL;
using RecoveryBLL.Exceptions;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;
using RecoveryBLL.Services;
using RecoveryCli;
using RecoveryDAL;
using RecoveryDAL.Configuration;

const string Usage = """
    rtcatalog - fingerprint catalog for the Recovery Toolkit (read-only: never changes the scanned disks)

    Usage:
      rtcatalog scan <folder or drive> [...]   List files, fingerprint possible duplicates, read image info, report
      rtcatalog hash                           Fingerprint whatever is still pending on the disks plugged in now
      rtcatalog report [--top N]               Totals per disk: unique / duplicated / not yet decided
      rtcatalog verify [--top N]               Check that duplicates really are identical: re-read N random groups (default 20) with SHA-256
      rtcatalog disks                          Catalogued disks and the drives plugged in now (with bus: USB/SATA/NVMe...)
      rtcatalog devices                        Physical disks: number, model, bus, HDD/SSD (needed for Linux mounts)
      rtcatalog merge <other-catalog.db>       Merge a catalog made on another PC into this one
      rtcatalog plan <main> [overflow ...]     Dry run: what would be copied where, and does it fit? (writes nothing)
      rtcatalog copy <main> [overflow ...]     Copy one clean set of everything onto the destination disks
      rtcatalog cleanup [folder ...]           Show what a crashed/killed run left behind (Linux mounts are freed, *.rtpart listed)
      rtcatalog wsl status                     Is WSL 2 available for Linux (ext4) disks?
      rtcatalog wsl scan <disk#> <part#> [fs]  Mount a Linux partition read-only via WSL, scan it, unmount (admin)

    Options:
      --env <file>       Settings file (default: .env searched from the current folder upwards)
      --db <file>        Use this SQLite catalog file instead of the one in the .env
      --exclude <name>   Skip folders with this name, or this full path (repeatable)
      --threads <n|auto> Readers per disk while hashing (default auto: 1 for HDD, more for SSD/NVMe)
      --sha256           Also store SHA-256 next to xxHash128
      --no-hash          scan: only list files (fingerprint later with 'hash')
      --no-media         scan/hash: skip reading image dimensions and EXIF
      --top <n>          report: also list the n largest duplicate groups

    plan / copy (destinations: a drive or folder, main first; add =<size> to cap one, e.g.  I:\=500GB):
      --no-duplicates    Do not copy identical extra copies to _Duplicates (default: copy them into leftover space)
      --reserve <size>   Keep this much free on every destination (default 1GB)
      --csv <file>       plan: also write every planned file (action, source, destination) to a CSV file
      --allow-pending    copy: start even if some files are not fingerprinted yet (they are treated as unique)
      --verify           copy: read every copy back and compare it with the source hash (slower)
      --no-wait          copy: do not wait for an unplugged disk (default: wait up to 30 minutes for it to come back)

    Deleting (old duplicates to make room, leftover *.rtpart files) always takes two steps:
      1. 'plan' / 'cleanup' list the files and show an approval code for exactly that list;
      2. run 'copy' / 'cleanup' with --approve-delete <code>, then type DELETE when asked.
      --approve-delete <code>   approve the listed deletions (a changed list has another code and is refused)

    Safety: the Windows drive (C:\) can only be used through a subfolder, e.g. C:\Recovery. Windows, Program Files,
    ProgramData, AppData and folders containing them (C:\Users, C:\Users\<name>) are refused, for scans and destinations.

    Stop any time with Ctrl+C; running the same command again continues where it stopped.
    Anything a stopped or crashed run still held is freed automatically the next time rtcatalog or the app starts.
    """;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help" or "/?")
{
    Console.WriteLine(Usage);
    return 0;
}

// ---------------------------------------------------------------- arguments
var command = args[0].ToLowerInvariant();
var positional = new List<string>();
var excludes = new List<string>();
string? envFile = null, dbFile = null;
int threads = 0, top = 0;   // threads 0 = auto
bool sha256 = false, noHash = false, noMedia = false;
bool noDuplicates = false, allowPending = false, verify = false, noWait = false;
string? approveDelete = null;
long reserve = ConsolidateOptions.DefaultReserve;
string? csvFile = null;
try
{
    for (int i = 1; i < args.Length; i++)
    {
        string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
        switch (args[i].ToLowerInvariant())
        {
            case "--env": envFile = Next(); break;
            case "--db": dbFile = Next(); break;
            case "--exclude": excludes.Add(Next()); break;
            case "--threads": var t = Next(); threads = t == "auto" ? 0 : Math.Clamp(int.Parse(t), 1, 64); break;
            case "--top": top = Math.Max(0, int.Parse(Next())); break;
            case "--sha256": sha256 = true; break;
            case "--no-hash": noHash = true; break;
            case "--no-media": noMedia = true; break;
            case "--no-duplicates": noDuplicates = true; break;
            case "--allow-pending": allowPending = true; break;
            case "--verify": verify = true; break;
            case "--no-wait": noWait = true; break;
            case "--approve-delete": approveDelete = Next(); break;
            case "--reserve": reserve = ParseSize(Next()); break;
            case "--csv": csvFile = Next(); break;
            default:
                if (args[i].StartsWith("--")) throw new ArgumentException($"Unknown option {args[i]}");
                positional.Add(args[i]);
                break;
        }
    }
}
catch (Exception ex) when (ex is ArgumentException or FormatException)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine("Run 'rtcatalog --help' for usage.");
    return 2;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    if (cts.IsCancellationRequested) return;   // second Ctrl+C: terminate
    e.Cancel = true;
    cts.Cancel();
    Console.Error.WriteLine("\nStopping after the current files... (Ctrl+C again to quit immediately)");
};

try
{
    // ---------------------------------------------------------------- composition root
    var settings = EnvSettingsLoader.Load(envFile, dbFile);
    var services = new ServiceCollection()
        .AddRecoveryDal(settings)
        .AddRecoveryBll()
        .AddSingleton<IOperationLog>(new ConsoleLog(settings.LogDirectory))
        .AddSingleton<IDeletionConfirmer, ConsoleDeletionConfirmer>()
        .BuildServiceProvider();
    var catalog = services.GetRequiredService<CatalogService>();
    catalog.Initialize();
    // Give back what a crashed or killed run left behind (Linux mounts, half-written copies). Reports only if it did something.
    var startupCleanup = services.GetRequiredService<RunStateService>().ReleaseStale();
    if (startupCleanup.DidSomething) Console.WriteLine();

    var progress = new ConsoleProgress();
    var hashOptions = new FingerprintOptions(Math.Max(1, threads), sha256, !noMedia, AutoThreads: threads == 0);
    T Get<T>() where T : notnull => services.GetRequiredService<T>();

    switch (command)
    {
        case "scan":
            if (positional.Count == 0) throw new RecoveryServiceException(@"scan needs at least one folder or drive, e.g.  rtcatalog scan F:\");
            Console.WriteLine($"Catalog: {catalog.Description}");
            foreach (var p in positional)
            {
                if (cts.IsCancellationRequested) break;
                var target = p.Length == 2 && p[1] == ':' ? p + "\\" : p;   // "F:" = drive root, not F:'s current folder
                Get<ScanService>().Scan(new ScanRequest(target, excludes), progress, cts.Token);
            }
            if (!noHash && !cts.IsCancellationRequested) Get<FingerprintService>().Run(hashOptions, progress, cts.Token);
            if (!cts.IsCancellationRequested) PrintReport(top);
            break;

        case "hash":
            Console.WriteLine($"Catalog: {catalog.Description}");
            Get<FingerprintService>().Run(hashOptions, progress, cts.Token);
            if (!cts.IsCancellationRequested) PrintReport(top);
            break;

        case "report":
            PrintReport(top);
            break;

        case "disks":
            PrintDisks();
            break;

        case "devices":
            PrintDevices();
            break;

        case "merge":
            if (positional.Count != 1) throw new RecoveryServiceException("merge needs exactly one catalog file");
            Get<MergeService>().Merge(positional[0], progress, cts.Token);
            break;

        case "wsl":
            RunWsl();
            break;

        case "cleanup":
            RunCleanup();
            break;

        case "verify":
            RunVerify(top > 0 ? top : 20);
            break;

        case "plan":
        case "copy":
            RunConsolidate(command == "copy");
            break;

        default:
            throw new RecoveryServiceException($"Unknown command '{args[0]}'. Run 'rtcatalog --help'.");
    }
    return cts.IsCancellationRequested ? 1 : 0;

    // ---------------------------------------------------------------- output
    void PrintReport(int topGroups)
    {
        var r = Get<ReportService>().GetReport();
        Console.WriteLine();
        Console.WriteLine($"Catalog: {catalog.Description}");
        Console.WriteLine();
        Console.WriteLine("Per disk");
        TablePrinter.Print(
            ["Disk", "Now on", "Last scan (UTC)", "Files", "Total", "Only copy", "Duplicated", "..also on other disk", "Not yet decided", "Empty", "Read errors"],
            r.Disks.Select(d => new[]
            {
                d.Disk.DisplayName, d.MountedRoot ?? "unplugged", Format.Stamp(d.Disk.LastScan), $"{d.Files:N0}",
                Format.Bytes(d.TotalBytes), Format.Bytes(d.OnlyCopyBytes), Format.Bytes(d.DuplicatedBytes),
                Format.Bytes(d.DuplicatedOnOtherDiskBytes), Format.Bytes(d.PendingBytes), $"{d.EmptyFiles:N0}", $"{d.ErrorFiles:N0}",
            }).ToList());
        Console.WriteLine("  Only copy = content found nowhere else.  Duplicated = the same content exists more than once (here or on another disk).");
        Console.WriteLine();
        Console.WriteLine("All disks together");
        var rows = r.Types.Select(t => new[]
        {
            t.MediaType, $"{t.Files:N0}", Format.Bytes(t.TotalBytes), Format.Bytes(t.UniqueBytes), Format.Bytes(t.RedundantBytes), Format.Bytes(t.PendingBytes),
        }).ToList();
        rows.Add(["TOTAL", $"{r.TotalFiles:N0}", Format.Bytes(r.TotalBytes), Format.Bytes(r.UniqueBytes), Format.Bytes(r.RedundantBytes), Format.Bytes(r.PendingBytes)]);
        TablePrinter.Print(["Type", "Files", "Total", "Unique data", "Extra copies", "Not yet decided"], rows);
        Console.WriteLine($"  Space needed to keep one copy of everything: {Format.Bytes(r.UniqueBytes)}"
            + (r.PendingBytes > 0 ? $" to {Format.Bytes(r.NeededMaxBytes)} (depends on the {Format.Bytes(r.PendingBytes)} not yet fingerprinted)" : ""));
        if (r.PendingUnpluggedDisks.Count > 0)
            Console.WriteLine($"  To finish fingerprinting, plug in and run 'rtcatalog hash': {string.Join(", ", r.PendingUnpluggedDisks.Select(d => d.DisplayName))}");
        else if (r.HasPendingOnPluggedInDisks)
            Console.WriteLine("  Run 'rtcatalog hash' to fingerprint the rest.");
        Console.WriteLine();

        var groups = Get<ReportService>().GetLargestDuplicateGroups(topGroups);
        if (groups.Count == 0) return;
        Console.WriteLine($"Largest duplicate groups (top {topGroups} by space taken by extra copies)");
        foreach (var (g, copies) in groups)
        {
            Console.WriteLine($"  {g.Copies} x {Format.Bytes(g.Size)}  (extra copies: {Format.Bytes(g.Size * (g.Copies - 1))})  {Format.Hex(g.FullHash)}");
            foreach (var (disk, path, modified) in copies)
                Console.WriteLine($"      {modified:yyyy-MM-dd HH:mm}  [{disk?.Serial ?? "?"}] {path}");
        }
        Console.WriteLine();
    }

    void PrintDisks()
    {
        var disks = Get<DiskService>();
        var mounted = disks.GetMountedRoots();
        Console.WriteLine($"Catalog: {catalog.Description}");
        Console.WriteLine();
        Console.WriteLine("Catalogued disks");
        TablePrinter.Print(["#", "Label", "Serial", "FS", "Size", "Now on", "Last complete scan (UTC)", "Files"],
            disks.GetCatalogued().Select(d => new[]
            {
                $"{d.Id}", d.Label.Length == 0 ? "(no label)" : d.Label, d.Serial, d.FileSystem, Format.Bytes(d.TotalBytes),
                mounted.TryGetValue(d.Id, out var root) ? root : "unplugged", Format.Stamp(d.LastScan), $"{d.FileCount:N0}",
            }).ToList());
        Console.WriteLine();
        Console.WriteLine("Plugged in now");
        TablePrinter.Print(["Drive", "Label", "FS", "Size", "Free", "Disk#", "Connection", "Kind", "Readers", "In catalog"],
            disks.GetPluggedInDrives().Select(s => new[]
            {
                s.Volume.Root, s.Volume.Label, s.Volume.FileSystem, Format.Bytes(s.Volume.TotalBytes), Format.Bytes(s.FreeBytes),
                s.Device?.Number.ToString() ?? "-", s.BusInfo?.Name ?? s.Device?.Bus.ToString() ?? "?", s.Device?.Kind ?? "?",
                $"{s.RecommendedThreads}", s.CatalogDisk != null ? "yes" : "no",
            }).ToList());
        foreach (var s in disks.GetPluggedInDrives().Where(s => s.BusInfo?.TrimWarning == true && s.Device?.IncursSeekPenalty == false))
            Console.WriteLine($"  Note {s.Volume.Root}: SSD with TRIM. Files deleted from it are usually wiped by the drive and cannot be carved back.");
    }

    void PrintDevices()
    {
        var dict = Get<DictionaryService>().Current;
        Console.WriteLine("Physical disks (use the number with 'rtcatalog wsl scan <disk#> <partition#>')");
        TablePrinter.Print(["Disk#", "Model", "Size", "Connection", "Kind", "TRIM", "Removable"],
            Get<DiskService>().GetPhysicalDevices().Select(d => new[]
            {
                $"{d.Number}", d.Model, Format.Bytes(d.SizeBytes),
                dict.BusTypes.TryGetValue(d.Bus, out var b) ? b.Name : d.Bus.ToString(), d.Kind,
                d.SupportsTrim switch { true => "yes", false => "no", _ => "?" }, d.Removable ? "yes" : "no",
            }).ToList());
    }

    void RunConsolidate(bool execute)
    {
        if (positional.Count == 0)
            throw new RecoveryServiceException($@"{command} needs at least one destination, main disk first, e.g.  rtcatalog {command} H:\ I:\");
        var destinations = positional.Select(p =>
        {
            var eq = p.LastIndexOf('=');
            return eq > 1 ? new DestinationRequest(p[..eq], ParseSize(p[(eq + 1)..])) : new DestinationRequest(p);
        }).ToList();
        var options = new ConsolidateOptions(destinations, !noDuplicates, reserve, allowPending, verify, approveDelete, WaitForUnplugged: !noWait);
        var service = Get<ConsolidationService>();
        Console.WriteLine($"Catalog: {catalog.Description}");
        try
        {
            if (!execute)
            {
                PrintPlan(service.Plan(options, progress, cts.Token));
                return;
            }
            var r = service.Execute(options, progress, cts.Token);
            Console.WriteLine();
            Console.WriteLine($"Copied:                  {r.Copied:N0} files, {Format.Bytes(r.CopiedBytes)}");
            Console.WriteLine($"Already on destination:  {r.AlreadyThere:N0} files (before this run)");
            if (r.Evicted > 0) Console.WriteLine($"Old duplicates removed:  {r.Evicted:N0} files, {Format.Bytes(r.EvictedBytes)}");
            if (r.DeletionsNotApproved > 0) Console.WriteLine($"Not deleted:             {r.DeletionsNotApproved:N0} old duplicates (not approved/confirmed); files that need that room wait");
            foreach (var f in r.FailingDisks) Console.WriteLine($"Failing disk skipped:    {f} (reads kept timing out: image it first)");
            if (r.WaitingForDisk > 0) Console.WriteLine($"Waiting for a disk:      {r.WaitingForDisk:N0} files (plug the disk in and run copy again)");
            if (r.NoRoom > 0) Console.WriteLine($"Did not fit:             {r.NoRoom:N0} files");
            if (r.Errors > 0) Console.WriteLine($"Errors:                  {r.Errors:N0} files (see the log)");
            if (!r.Completed) Console.WriteLine("Stopped. Run the same command again to continue.");
            else Console.WriteLine($"Run 'rtcatalog plan {string.Join(' ', positional)}' to check that nothing is left.");
        }
        catch (OperationCanceledException)
        {
            ConsoleProgress.ClearLine();
            Console.WriteLine("Stopped. Run the same command again to continue.");
        }
    }

    void PrintPlan(CopyPlan plan)
    {
        Console.WriteLine();
        Console.WriteLine("Copy plan (dry run: nothing has been written)");
        Console.WriteLine();
        TablePrinter.Print(["Destination", "Folder", "FS", "Free", "Usable", "Planned", "Files", "Old duplicates removed"],
            plan.Destinations.Select(d => new[]
            {
                d.Name, d.FullPath, d.FileSystem, Format.Bytes(d.FreeBytes), Format.Bytes(d.UsableBytes),
                Format.Bytes(d.PlannedBytes), $"{d.PlannedFiles:N0}", d.EvictedBytes > 0 ? Format.Bytes(d.EvictedBytes) : "-",
            }).ToList());
        Console.WriteLine();
        string[] Row(string what, CopyAction a) => [what, $"{plan.Files(a):N0}", Format.Bytes(plan.Bytes(a))];
        TablePrinter.Print(["What", "Files", "Size"],
        [
            Row("Unique -> main folders (Photos, Videos, ...)", CopyAction.Keep),
            Row("Older versions -> _OlderVersions", CopyAction.OlderVersion),
            noDuplicates ? Row("Duplicates (switched off, not copied)", CopyAction.SkippedDuplicate) : Row("Duplicates -> _Duplicates", CopyAction.Duplicate),
            Row("Already on a destination", CopyAction.AlreadyThere),
            Row("No room", CopyAction.NoRoom),
        ]);
        Console.WriteLine();

        bool DupNoRoom(PlanItem i) => i.Action == CopyAction.NoRoom && i.Note == PlanNotes.DuplicateNoRoom;
        var unique = plan.Bytes(CopyAction.Keep) + plan.Bytes(CopyAction.OlderVersion)
                     + plan.Items.Where(i => i.Action == CopyAction.NoRoom && !DupNoRoom(i)).Sum(i => i.Size);
        var dupes = plan.Bytes(CopyAction.Duplicate) + plan.Bytes(CopyAction.SkippedDuplicate) + plan.Items.Where(DupNoRoom).Sum(i => i.Size);
        var usable = plan.Destinations.Sum(d => d.UsableBytes);
        Console.WriteLine($"  {Format.Bytes(unique)} unique, {Format.Bytes(dupes)} duplicates, {plan.Files(CopyAction.OlderVersion):N0} older versions"
            + (plan.UniqueFits ? $" -> fits ({Format.Bytes(usable)} usable)." : $" -> DOES NOT FIT in {Format.Bytes(usable)} usable. Add an overflow disk."));
        var dupNoRoom = plan.Items.Count(DupNoRoom);
        if (plan.UniqueFits && dupNoRoom > 0)
            Console.WriteLine($"  {dupNoRoom:N0} duplicates do not fit. That is fine: one copy of their content is kept.");
        Console.WriteLine($"  To copy: {plan.FilesToCopy:N0} files, {Format.Bytes(plan.BytesToCopy)}.");
        foreach (var (disk, files, bytes) in plan.WaitingDisks)
            Console.WriteLine($"  Needs {disk.DisplayName} plugged in: {files:N0} files, {Format.Bytes(bytes)}");
        foreach (var w in plan.Warnings) Console.WriteLine("  Note: " + w);
        if (!plan.Deletions.IsEmpty)
        {
            Console.WriteLine();
            ConsoleDeletionConfirmer.PrintList(plan.Deletions);
            Console.WriteLine($"  Step 1 of 2: to approve exactly this list, run copy with  --approve-delete {plan.Deletions.Code}");
            Console.WriteLine("  Step 2 of 2: copy asks you to type DELETE before anything is removed. Without both, nothing is deleted.");
        }
        if (csvFile != null) WritePlanCsv(plan, csvFile);
        Console.WriteLine();
    }

    void WritePlanCsv(CopyPlan plan, string path)
    {
        PlanExport.WriteCsv(plan, Get<DiskService>().GetCatalogued().ToDictionary(d => d.Id, d => d.DisplayName), path);
        Console.WriteLine($"  Plan written to {Path.GetFullPath(path)}");
    }

    void RunVerify(int groups)
    {
        Console.WriteLine($"Catalog: {catalog.Description}");
        Console.WriteLine($"Re-reading every copy of {groups} random duplicate groups (files up to {Format.Bytes(DuplicateCheckService.DefaultMaxFileSize)}) and comparing them with SHA-256.");
        var r = Get<DuplicateCheckService>().CheckSample(groups, progress, cts.Token);
        foreach (var g in r.Groups)
        {
            var verdict = g.Identical ? "IDENTICAL" : g.Different ? "DIFFERENT" : "not checked";
            Console.WriteLine();
            Console.WriteLine($"  {verdict,-11}  {Format.Bytes(g.Size)} x {g.Copies.Count}");
            foreach (var c in g.Copies)
                Console.WriteLine($"      {(c.Sha256 ?? "-")[..Math.Min(16, (c.Sha256 ?? "-").Length)],-16}  {c.FullPath}{(c.Problem != null ? "   (" + c.Problem + ")" : "")}");
        }
        Console.WriteLine();
        Console.WriteLine($"Result: {r.Identical} identical, {r.Different} different, {r.NotChecked} not checked (copies unreadable or disk unplugged). Read {Format.Bytes(r.BytesRead)}.");
        if (r.Different > 0) Console.WriteLine("Different groups mean a file changed after it was scanned (or a read error): scan those disks again.");
    }

    void RunCleanup()
    {
        var runState = Get<RunStateService>();
        Console.WriteLine($"Run-state journal: {runState.Location}");
        var folders = positional.Count > 0 ? positional : null;
        if (approveDelete != null)
        {
            var done = runState.DeleteLeftovers(folders, approveDelete);
            Console.WriteLine($"Deleted {done.TempFilesDeleted:N0} half-written copies.");
            foreach (var p in done.Problems) Console.WriteLine("  " + p);
            return;
        }
        var problems = new List<string>();
        var leftovers = runState.FindLeftovers(folders, problems);
        foreach (var p in problems) Console.WriteLine("  " + p);
        var held = runState.Current().Where(l => l.ProcessId != Environment.ProcessId).ToList();
        if (held.Count > 0)
        {
            Console.WriteLine("Held by runs that are still active (or not freed yet):");
            foreach (var l in held)
                Console.WriteLine($"  {l.Kind} {l.Target}  (process {l.ProcessId}, since {Format.Stamp(l.Since)} UTC)");
        }
        if (leftovers.IsEmpty)
        {
            Console.WriteLine("No half-written copies left behind.");
            if (folders == null) Console.WriteLine("Tip: 'rtcatalog cleanup H:\\Backup' also looks for leftover *.rtpart files in a folder the journal does not know.");
            return;
        }
        ConsoleDeletionConfirmer.PrintList(leftovers);
        var again = folders == null ? "" : " " + string.Join(' ', folders.Select(f => f.Contains(' ') ? "\"" + f + "\"" : f));
        Console.WriteLine($"Step 1 of 2: to approve exactly this list, run  rtcatalog cleanup{again} --approve-delete {leftovers.Code}");
        Console.WriteLine("Step 2 of 2: you are then asked to type DELETE. Without both, nothing is deleted.");
    }

    void RunWsl()
    {
        var linux = Get<LinuxDiskService>();
        var sub = positional.FirstOrDefault()?.ToLowerInvariant() ?? "status";
        if (sub == "status")
        {
            Console.WriteLine($"WSL installed: {(linux.IsWslInstalled ? "yes" : "no  (install with: wsl --install)")}");
            if (linux.IsWslInstalled) Console.WriteLine($"Distributions: {string.Join(", ", linux.Distributions)}");
            Console.WriteLine($"Running as administrator: {(linux.IsElevated ? "yes" : "no  (needed to mount Linux disks)")}");
            Console.WriteLine($"Filesystems: {string.Join(", ", LinuxDiskService.SupportedFileSystems)}");
            return;
        }
        if (sub != "scan" || positional.Count < 3 || !int.TryParse(positional[1], out var disk) || !int.TryParse(positional[2], out var part))
            throw new RecoveryServiceException("Usage: rtcatalog wsl scan <disk#> <partition#> [ext4]   (disk numbers: rtcatalog devices)");
        var fs = positional.Count > 3 ? positional[3].ToLowerInvariant() : "ext4";
        var mount = linux.Mount(disk, part, fs);
        try
        {
            Get<ScanService>().Scan(new ScanRequest(mount.Path, excludes, mount.Identity), progress, cts.Token);
            if (!noHash && !cts.IsCancellationRequested) Get<FingerprintService>().Run(hashOptions, progress, cts.Token);
        }
        finally
        {
            linux.Unmount(mount);
        }
        if (!cts.IsCancellationRequested) PrintReport(top);
    }
}
catch (Exception ex) when (ex is RecoveryServiceException or ConfigurationException or ArgumentException)
{
    ConsoleProgress.ClearLine();
    Console.Error.WriteLine("Error: " + ex.Message);
    return 2;
}

/// <summary>"500GB", "1.8TB", "750MB", "1024" (bytes). Binary units, like Windows Explorer.</summary>
static long ParseSize(string text)
{
    var t = text.Trim().ToUpperInvariant().Replace(" ", "");
    (string Suffix, long Factor)[] units = [("TB", 1L << 40), ("GB", 1L << 30), ("MB", 1L << 20), ("KB", 1L << 10), ("T", 1L << 40), ("G", 1L << 30), ("M", 1L << 20), ("K", 1L << 10), ("B", 1)];
    foreach (var (suffix, factor) in units)
        if (t.EndsWith(suffix) && double.TryParse(t[..^suffix.Length], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0)
            return (long)(v * factor);
    if (long.TryParse(t, out var bytes) && bytes >= 0) return bytes;
    throw new ArgumentException($"Not a size: {text} (use e.g. 500GB or 1.5TB)");
}
