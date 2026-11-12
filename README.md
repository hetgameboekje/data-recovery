# data-recovery
For recovering data off old disks: catalog every disk first, find out what is unique and what is a copy, then (later) copy one clean set.

See [PLAN.md](PLAN.md) for the full plan, the architecture decisions, licenses of third-party code, and open points.
The PowerShell RecoveryToolkit stays in use until each feature has been ported.

## Projects

| Project | What | Output |
|---|---|---|
| `src/RecoveryBLL` | Business logic: models, DTOs, repository/data-source **interfaces**, services, validation | library (platform-neutral) |
| `src/RecoveryDAL` | Data access: SQLite + SQL Server repositories, `.env` settings, XML dictionaries (DOM), disks/volumes/WSL | library (Windows) |
| `src/RecoveryUI` | Desktop app, WinForms MVC (views / controllers / view models) | `RecoveryToolkit.exe` |
| `src/RecoveryCli` | Command line, same services | `rtcatalog.exe` |
| `tests/RecoveryBLL.Tests` | xUnit: config, XML, hashing, EXIF, full pipeline on SQLite (and SQL Server with `RT_TEST_SQLSERVER=1`) | |
| `config/dictionaries.xml` | File types, folders to skip, connection types, junk rules | copied next to the exes |

Dependencies point one way: `UI/CLI → BLL ← DAL`. The BLL never references SQLite, SQL Server, WinForms or Windows APIs.

## Setup

1. Install the .NET 10 SDK (Visual Studio 2026 includes it).
2. Copy `.env.example` to `.env` and adjust it. Without a `.env` the defaults are used: SQLite file `catalog.db` in the current folder.
3. Open `data-recovery.slnx` in Visual Studio (start project: **RecoveryUI**) or VS Code (F5 → "Recovery Toolkit (WinForms)").

```
dotnet build data-recovery.slnx
dotnet test  data-recovery.slnx
dotnet publish src/RecoveryUI  -c Release -o publish     # single RecoveryToolkit.exe
dotnet publish src/RecoveryCli -c Release -o publish     # single rtcatalog.exe
```

## Settings (`.env`)

| Key | Meaning |
|---|---|
| `DB_PROVIDER` | `sqlite` (default) or `sqlserver` |
| `DB_CONNECTION` | SQLite: `Data Source=catalog.db` (relative to the `.env`). SQL Server: e.g. `Server=(localdb)\MSSQLLocalDB;Database=RecoveryCatalog;Trusted_Connection=True;TrustServerCertificate=True` (database is created automatically) |
| `DB_PASSWORD` | Optional; use as `${DB_PASSWORD}` inside `DB_CONNECTION` |
| `DICTIONARIES_FILE` | Default `config/dictionaries.xml` |
| `LOG_DIR` | Default `%LOCALAPPDATA%\RecoveryToolkit\logs` |
| `NET_*`, `BACKLOG_DIR`, `OVERRIDE_DIR` | Network export (planned, phase 9) |

Environment variables with the same name override the file. `.env`, catalogs, logs and keys are git-ignored.

## Desktop app

- **Simple mode** (default): Home shows the totals and the next step; *Catalog disks* = tick drives, press Start; *Report* = how much is unique, how much is duplicated, how much space one clean copy needs.
- **Expert mode** (button top-right, remembered): disk numbers, bus (USB/SATA/NVMe/SD…), HDD/SSD, TRIM warnings, readers per disk, SHA-256, excludes, largest duplicate groups, Linux (ext4) disks via WSL, and a live log.
- Cataloguing is **read-only**. Stop any time; starting again continues where it stopped.

## Command line (`rtcatalog`)

```
rtcatalog scan F:\                  list F:, fingerprint possible duplicates, read image info, print report
rtcatalog scan F:\ G:\ E:\Photos    several disks/folders in one go
rtcatalog hash                      finish fingerprinting on whatever catalogued disks are plugged in
rtcatalog report --top 20           totals per disk + the 20 biggest duplicate groups
rtcatalog disks                     catalogued disks + plugged-in drives with connection type
rtcatalog devices                   physical disks: number, model, bus, HDD/SSD, TRIM
rtcatalog merge other-pc.db         merge a catalog made on another PC (works into SQL Server too)
rtcatalog plan H:\ I:\               dry run: what goes where on main H: then overflow I:, and does it fit?
rtcatalog plan H:\ I:\=500GB --csv plan.csv   cap I: at 500 GB, write every planned file to a CSV
rtcatalog copy H:\ I:\               copy one clean set (stop any time; run again to continue)
rtcatalog cleanup                   free what a crashed run left behind (Linux mounts, *.rtpart copies)
rtcatalog wsl status                is WSL available for Linux disks?
rtcatalog wsl scan 3 1 ext4         mount PhysicalDrive3 partition 1 read-only via WSL, catalog, unmount (admin)
```

Options: `--env <file>`, `--db <sqlite file>` (overrides the `.env`), `--exclude <name or path>`, `--threads <n|auto>` (default auto: 1 for HDD, more for SSD/NVMe), `--sha256`, `--no-hash`, `--no-media`, `--top <n>`.

`plan`/`copy` options: `--no-duplicates`, `--reserve <size>` (default 1GB), `--csv <file>`, `--allow-pending`, `--verify`.

## Safety

- **Protected folders:** the Windows drive (`C:\`) can only be used through a subfolder such as `C:\Recovery`. Windows, Program Files, ProgramData, every AppData, and folders that contain them (`C:\Users`, `C:\Users\<name>`) are refused, both for scanning and as a destination. Other drives may be used whole.
- **Windows keeps working:** a destination on the Windows drive always keeps at least 20 GB free.
- **After a crash or kill:** each start of rtcatalog or the app frees what the stopped run held: it unmounts WSL Linux disks (needs administrator) and lists half-written `*.rtpart` copies. `rtcatalog cleanup` shows the same on request. Two runs never write the same destination.
- **Every deletion takes two steps:** (1) `plan`/`cleanup` list the files with an approval code, and you approve exactly that list with `--approve-delete <code>`; (2) you type `DELETE` when asked (in the app: a second dialog). Each file is re-checked just before it is removed. Without both steps nothing is deleted.
- **Unplugged disks:** a copy waits up to 30 minutes for the disk to come back, on any port or drive letter (`--no-wait` skips it instead). Hashing stops that disk without marking its files missing. A scan keeps what it listed.
- **Catalog on a source disk is refused:** the toolkit won't write to a disk you recover from. Put `catalog.db` and the logs elsewhere (`DB_CONNECTION`, `LOG_DIR`, `--db`).
- **Long runs:** the PC stays awake while scanning, hashing or copying. Reads that hang on a bad sector are cut off after 60 s; after 3 on one disk, that disk is skipped (image it first).

## Copying (Phase 2)

Source disks are only read. On the destinations you get:

| Folder | What |
|---|---|
| `Photos\`, `Videos\`, `Documents\`, … | one copy of all content, in its original folder structure |
| `_OlderVersions\<disk>\…` | same folder and name as a newer file, but different content |
| `_Duplicates\<disk>\…` | identical extra copies (lowest priority; skip with `--no-duplicates`) |

Unique files get the space first. Old `_Duplicates` files are removed only to make room for a unique file, and only if that content is still kept elsewhere on the destination. Nothing on a destination is overwritten. Copies are written as `.rtpart`, checked against the catalog hash, then renamed, so stopping is always safe.

## How it avoids reading everything

1. A file whose size no other catalogued file has can't be a duplicate, so it's never read.
2. Files that share a size get a quick hash of their first and last 64 KB (the whole file if it's ≤ 128 KB).
3. Only files whose quick hash still matches are read in full (xxHash128).

This is checked across **all** catalogued disks; `report` lists disks that must be plugged in to finish. Disks are identified by volume serial + size (Linux: filesystem UUID), never by drive letter, and paths are stored relative to the volume root.
