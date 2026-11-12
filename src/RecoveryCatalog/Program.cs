using RecoveryCatalog;

const string Usage = """
    rtcatalog - fingerprint catalog for the Recovery Toolkit (phase 1, read-only: never changes your files)

    Usage:
      rtcatalog scan <folder or drive> [...]   List files, then fingerprint possible duplicates and read image info
      rtcatalog hash                           Fingerprint whatever is still pending on the disks plugged in now
      rtcatalog report [--top N]               Totals per disk: unique / duplicated / not yet decided
      rtcatalog disks                          Catalogued disks and the drives plugged in now
      rtcatalog merge <other-catalog.db>       Merge a catalog made on another PC into this one

    Options:
      --db <file>        Catalog file (default: catalog.db in the current folder, or %RTCATALOG_DB%)
      --exclude <name>   Skip folders with this name, or this full path (repeatable)
      --threads <n>      Readers per disk while hashing (default 1; use 4+ only for SSD/NVMe)
      --sha256           Also store SHA-256 next to xxHash128 for fingerprinted files
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

var command = args[0].ToLowerInvariant();
var positional = new List<string>();
var excludes = new List<string>();
string db = Environment.GetEnvironmentVariable("RTCATALOG_DB") is { Length: > 0 } envDb ? envDb : "catalog.db";
int threads = 1, top = 0;
bool sha256 = false, noHash = false, noMedia = false;

try
{
    for (int i = 1; i < args.Length; i++)
    {
        string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
        switch (args[i].ToLowerInvariant())
        {
            case "--db": db = Next(); break;
            case "--exclude": excludes.Add(Next()); break;
            case "--threads": threads = Math.Clamp(int.Parse(Next()), 1, 64); break;
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
    if (cts.IsCancellationRequested) return;   // second Ctrl+C: let it terminate
    e.Cancel = true;
    cts.Cancel();
    Log.Info("Stopping after the current files... (press Ctrl+C again to quit immediately)");
};

try
{
    using var cat = new Catalog(db);
    Log.FilePath = Path.Combine(Path.GetDirectoryName(cat.FilePath)!, Path.GetFileNameWithoutExtension(cat.FilePath) + "-log.txt");
    var hashOpts = new Hasher.Options(threads, sha256, !noMedia);

    switch (command)
    {
        case "scan":
        {
            if (positional.Count == 0) throw new ArgumentException("scan needs at least one folder or drive, e.g.  rtcatalog scan F:\\");
            Log.Info($"Catalog: {cat.FilePath}");
            var scanned = new HashSet<long>();
            foreach (var p in positional)
            {
                if (cts.IsCancellationRequested) break;
                var target = p.Length == 2 && p[1] == ':' ? p + "\\" : p;   // "F:" means the drive root, not F:'s current folder
                var r = Scanner.Run(cat, target, excludes, cts.Token);
                Log.Info($"Scanned {Path.GetFullPath(target)}: {r.Files:N0} files, {Fmt.Bytes(r.Bytes)}"
                    + (r.DirErrors > 0 ? $", {r.DirErrors:N0} unreadable folder(s) (see log)" : "")
                    + (r.MarkedMissing > 0 ? $", {r.MarkedMissing:N0} catalogued file(s) no longer present" : "")
                    + (r.Completed ? "" : " - INTERRUPTED, run again to continue"));
                var root = Path.GetPathRoot(Path.GetFullPath(target))!;
                if (Volumes.Identify(root) is { } v) scanned.Add(cat.EnsureDisk(v));
            }
            if (!noHash && !cts.IsCancellationRequested)
            {
                // Fingerprint the scanned disks; also any other plugged-in catalogued disk whose files now collide.
                Hasher.Run(cat, hashOpts, cts.Token);
            }
            if (!cts.IsCancellationRequested)
            {
                Console.WriteLine();
                Report.Print(cat, top);
            }
            break;
        }
        case "hash":
            Log.Info($"Catalog: {cat.FilePath}");
            Hasher.Run(cat, hashOpts, cts.Token);
            if (!cts.IsCancellationRequested) { Console.WriteLine(); Report.Print(cat, top); }
            break;
        case "report":
            Report.Print(cat, top);
            break;
        case "disks":
            Report.Disks(cat);
            break;
        case "merge":
        {
            if (positional.Count != 1) throw new ArgumentException("merge needs exactly one catalog file");
            var (d, f) = cat.Merge(positional[0]);
            Log.Info($"Merged {positional[0]} into {cat.FilePath}: {d:N0} disk record(s), {f:N0} file record(s) added or updated.");
            break;
        }
        default:
            throw new ArgumentException($"Unknown command '{args[0]}'");
    }
    return cts.IsCancellationRequested ? 1 : 0;
}
catch (Exception ex) when (ex is ArgumentException or DirectoryNotFoundException or FileNotFoundException or InvalidOperationException or IOException)
{
    Progress.ClearLine();
    Console.Error.WriteLine("Error: " + ex.Message);
    return 2;
}
