# data-recovery
For recovering data off old disks: catalog every disk first, find out what is unique and what is a copy, then copy one clean set onto your backup disks.

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
dotnet test data-recovery.slnx
dotnet publish src/RecoveryUI -c Release -o publish
dotnet publish src/RecoveryCli -c Release -o publish
```

The two `publish` lines create `publish\RecoveryToolkit.exe` and `publish\rtcatalog.exe`: single files that run without installing .NET.

## How to run it

All commands below work in both **Command Prompt** and **PowerShell**. Run them in the repository folder.

### First time

```
dotnet --version
copy .env.example .env
dotnet build data-recovery.slnx
```

1. `dotnet --version` must print `10.x`. If it doesn't, install the .NET 10 SDK.
2. `copy` creates your settings file. It's optional: without it, the catalog is `catalog.db` in the current folder.
3. The build should end with "Build succeeded".

**Where the catalog lives matters.** The toolkit writes its catalog, logs and journal while it works. Keep them on a disk you are **not** recovering from: either the Windows drive (the default) or your backup disk. Set this with `DB_CONNECTION` / `LOG_DIR` in `.env`, or `--db` on the command line. Scanning the disk that holds the catalog is refused, because writing there could overwrite deleted files.

### Desktop app (recommended)

```
dotnet run --project src/RecoveryUI
```

You can also press F5 in Visual Studio (start project **RecoveryUI**) or VS Code ("Recovery Toolkit (WinForms)"), or start `RecoveryToolkit.exe` after `dotnet publish` (see Setup). Then follow the three steps on the Home page:

1. **Catalog disks.** Plug in a disk, tick it, press Start. This only reads the disk. Repeat for every disk, one at a time if you like; the catalog remembers them all.
2. **Report.** See how much is unique and how much is duplicated. If it says something is "not yet decided", press "Fingerprint the rest", or plug in the disk it names. **Check duplicates** re-reads every copy of 20 random duplicate groups and compares them byte for byte (SHA-256, independent of the catalog's own hash), so you can see for yourself that duplicates really are identical. It is read-only.
3. **Copy.**
   1. Add your backup disks: the main disk first, then overflow disks.
   2. Press **Check the plan**. This is a dry run that writes nothing.
   3. Read the verdict, then press **Start copying**.

   Stop any time; starting again continues where it stopped.

Turn on **Expert mode** (top right) for disk numbers and bus types, more options, the CSV export, Linux (WSL) disks and the live log.

### Command line

Publish the command-line tool once:

```
dotnet publish src/RecoveryCli -c Release -o publish
```

After that, `publish\rtcatalog` runs it. (Without publishing, `dotnet run --project src/RecoveryCli -- <command>` does the same, but slower.)

A typical run, with source disks F: and G: and backup disks H: (main) and I: (overflow):

```
publish\rtcatalog scan F:\
publish\rtcatalog scan G:\
publish\rtcatalog report --top 20
publish\rtcatalog verify
publish\rtcatalog plan H:\ I:\ --csv plan.csv
publish\rtcatalog copy H:\ I:\
```

1. `scan` catalogs and fingerprints one source disk. It only reads the disk. Repeat it for every source disk.
2. `report` shows what is unique and what is duplicated. `verify` re-reads 20 random duplicate groups and confirms they are byte-for-byte identical (`--top 50` for more).
3. `plan` is a dry run: what goes where, and whether it fits. It writes nothing; `plan.csv` lists every file.
4. `copy` does the copy. Ctrl+C stops it; running the same command again continues where it stopped.

`rtcatalog --help` lists every command and option. The full list is under [Command line](#command-line-rtcatalog) below.

### Good to know
- **Administrator rights** are only needed for Linux (ext4) disks via WSL. Start the app or terminal "as administrator" for those; everything else works as a normal user.
- **Before long runs:** set sleep to "Never" if you can, and pause Windows Update. The toolkit keeps the PC awake while it works, but it cannot stop an update restart. After a restart, just run the same step again.
- **Unplugged a disk by accident?** A copy waits up to 30 minutes for it to come back, on any port. Fingerprinting simply stops for that disk and leaves its files untouched.
- **Nothing is deleted without two confirmations:** first you approve the exact list (by its code), then you confirm again right before deletion.
- **After a crash or a killed run:** just start the app or rtcatalog again. It frees what the run left behind and lists leftover half-written copies (`rtcatalog cleanup`).

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
