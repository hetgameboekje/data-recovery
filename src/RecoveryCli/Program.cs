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
      rtcatalog disks                          Catalogued disks and the drives plugged in now (with bus: USB/SATA/NVMe...)
      rtcatalog devices                        Physical disks: number, model, bus, HDD/SSD (needed for Linux mounts)
      rtcatalog merge <other-catalog.db>       Merge a catalog made on another PC into this one
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

    Stop any time with Ctrl+C; running the same command again continues where it stopped.
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
        .BuildServiceProvider();
    var catalog = services.GetRequiredService<CatalogService>();
    catalog.Initialize();

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
catch (Exception ex) when (ex is RecoveryServiceException or ConfigurationException)
{
    ConsoleProgress.ClearLine();
    Console.Error.WriteLine("Error: " + ex.Message);
    return 2;
}
