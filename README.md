# data-recovery
For recovering data off old disk.

See [PLAN.md](PLAN.md) for the full plan. The PowerShell RecoveryToolkit stays in use until each feature has been ported.

## rtcatalog (phase 1: fingerprint catalog)

A command-line tool that reads every disk once and records a content fingerprint of each file in a SQLite file (`catalog.db`). It is **read-only**: it never changes, moves or deletes anything on the disks it scans.

### Build

Requires the .NET 10 SDK.

```
cd src\RecoveryCatalog
dotnet publish -c Release -o ..\..\publish
```

This produces a single `publish\rtcatalog.exe` that runs on any 64-bit Windows PC without .NET installed. Copy it to the other PC to catalog the disks there.

### Use

```
rtcatalog scan F:\                  list F:, fingerprint possible duplicates, read image info, print report
rtcatalog scan F:\ G:\ E:\Photos    several disks/folders in one go
rtcatalog hash                      finish fingerprinting on whatever catalogued disks are plugged in
rtcatalog report --top 20           totals per disk + the 20 biggest duplicate groups
rtcatalog disks                     which disks are catalogued, which are plugged in
rtcatalog merge other-pc.db         merge the catalog made on the other PC
```

Options: `--db <file>` (default `catalog.db` in the current folder), `--exclude <folder name or path>`, `--threads <n>` (readers per disk; keep 1 for HDDs, 4+ for SSD/NVMe), `--sha256`, `--no-hash`, `--no-media`.

Ctrl+C stops cleanly. Running the same command again continues: files that haven't changed since the last scan (same path, size and modified time) keep their fingerprints and aren't read again.

### How it avoids reading everything

1. A file whose size no other catalogued file has can't be a duplicate, so it's never read.
2. Files that share a size get a quick hash of their first and last 64 KB (the whole file if it's ≤ 128 KB).
3. Only files whose quick hash still matches are read in full (xxHash128).

The checks cover **all** catalogued disks, so a file on a disk scanned earlier can become a candidate when a new disk is added. `report` lists the disks that need to be plugged in for that.

Disks are identified by volume serial number + size, not by drive letter, so a disk is recognised when it comes back as a different letter or on another PC. Paths are stored relative to the volume root.

Skipped automatically: `$RECYCLE.BIN`, `System Volume Information`, `_RecoveryToolkit` (the toolkit's logs and half-sorted PhotoRec output), `*.rtpart` partial copies, the page/hibernation files, and junctions/symlinks.

### Database

`files` holds one row per file: disk, relative path, size, modified time, extension, media type, quick hash, full hash, optional SHA-256, image width/height, EXIF camera and date taken, read errors, first/last seen, and a missing flag. The indexes are `(size, quick_hash)` and `(full_hash)`. Phases 2–4 (copy plan, junk/preview detection, corrupt-file check) build on this table.
