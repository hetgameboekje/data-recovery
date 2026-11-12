# Recovery Toolkit – Plan & Project Notes

_Last updated: 2026-11-12 (architecture restructure)_

This file records the requests and questions so far, the current PowerShell toolkit, and the plan for the next version.

---

## 1. Requests & questions so far (history)

### 2026-11-11: original script and update
- A first script, `recoverscript.ps1`, was made. It checks the health of a USB 3.0 HDD, recovers photos/videos/audio with PhotoRec from the source disk, and sorts them on a target disk.
- It was then expanded into the GUI tool `RecoveryToolkit.ps1` + `RecoveryToolkit.cmd`.
- There are two copies on the Desktop:
  - `Desktop\data\` – **older** version (16:50, ~158 KB). **No resume support.**
  - `Desktop\file management\` – **newer** version (23:36, ~210 KB). Has resume, overflow destinations, journal and `.rtpart` safe copies. **This is the version to use.**

### 2026-11-12: "PC rebooted during recovery – check where it was left and continue with the best script"
- **Cause of the reboot:** Windows Update (MoUsoCoreWorker / TrustedInstaller) at 02:25. It was not a crash.
- **State found:**
  - The run used the old `data` script.
  - PhotoRec on Disk 3 (ASMT 2115, 932 GB) finished at 01:24 and recovered **27,755 files (538.8 GB)**: Photos 23,497 / Videos 3,163 / Audio 1,089 / Other 6.
  - The script was sorting files out of `G:\_RecoveryToolkit\deep-raw-20261111-170326` into `G:\Photos`, `G:\Videos`, `G:\Audio`, `G:\Other` when the reboot happened. About 22,000 files (~297 GB) were still unsorted.
  - No partial files were left: the moves are same-drive renames, so nothing was lost.
- **What was done:**
  - The newer script's own sorting step (`Drain-Raw`) was run headlessly on the leftover folder, without re-running the 8-hour PhotoRec scan.
  - The run moves files, uses Organize = Type, and checks duplicates against everything already on G:. It deletes the raw folder when it's empty.
  - Log: `G:\_RecoveryToolkit\log-20261112-111844.txt`
  - Helper used: a temporary script in the Claude scratchpad that extracts `$WorkerCode` from `RecoveryToolkit.ps1` and runs a `DrainOnly` job.
- **Status at the time of writing:** still running, with ~10,800 of ~22,000 files left in the raw folder.
- **Why not just press Start in the new GUI?** The old run never wrote the new script's `state.json` / `photorec.ses`. "Continue" would therefore have started a full new PhotoRec pass.

### 2026-11-12: "Second disk to combine, a 5 TB disk, 2 USB 3.0 ports + Icy Box dock with 2 USB 3.0. How to connect, and how to keep the 2 TB HDD from overflowing?"
- **Connect the disk you're recovering from directly to the laptop.** Put the destination disks in the Icy Box dock, powered by its own adapter.
  - The source is read for hours and a dropout restarts PhotoRec, so it needs the most stable link.
  - 2.5" bus-powered drives should go on a laptop port.
  - Bandwidth is fine: the dock shares one 5 Gbps link, which is far more than HDDs need, and the script writes to one destination at a time.
- **Preventing overflow with the current script:**
  1. Destination = 2 TB disk; **Overflow = G:\\** (the SanDisk 1 TB).
  2. Keep duplicate skipping on. It checks against files on **all** destinations, including overflow, so the earlier recovery is merged automatically.
  3. When everything is full, the script **pauses**. Add another overflow disk, keep "Continue an interrupted run" ticked, and press Start.
  4. Tick only Photos/Videos/Audio (not "Everything"). Set a minimum size (~100 KB) to cut thumbnails and cache images.
  5. PhotoRec reads the whole 5 TB surface, so the output can be well over 1 TB (old deleted and overwritten data).
- Pause Windows Update and set sleep to "Never" before overnight runs.
- Don't unplug G: while a sort is running.

### 2026-11-12: "New setup, more disks, many copies – plan a new version"
**Available disks:**
- 1 TB HDD (free)
- 2 TB 2.5" HDD
- SanDisk Extreme 1 TB (G:)
- Several M.2 NVMe / SATA disks inside another PC

**Wishes:**
1. A script that scans a "controller" disk, fingerprints all files (content hash), and saves the fingerprints in a database-like file. It must handle big data and use indexes for fast lookups.
2. Files with 100% identical content go to a **separate duplicates folder** instead of the main folders or overflow. That folder is only overwritten when both the backup disk and the overflow disk are full. This feature can be switched on or off.
3. Manual copies on some disks didn't contain the **most recent versions** of media. The newest version must win.
4. **Cleanup:** game assets, program pictograms/icons, Minecraft skins.
5. **New "Corrupt files" tab:**
   - Scan a folder.
   - Choose delete / recover / move to a subfolder.
   - After scanning, corrupt files are moved to a subfolder straight away, so nothing stays stuck if something goes wrong.
   - Recovery can end in three ways: the script quits, it succeeds, or the file is unrecoverable.
   - A successfully repaired file is moved back to the folder it came from.
6. **Question:** is PowerShell still the right choice, or C, Delphi or something better suited to data and disk recovery?

### 2026-11-12: "Low-quality preview pictures"
- The `_Duplicates` eviction rule (Phase 2) is **confirmed**.
- Recovery often brings back low-quality pictures that look like previews. These should be filtered out of the folders, with a choice of **move, delete or upscale**. Added as Phase 3b.

### 2026-11-12: "Work with SOLID and a 3-layer architecture, like AccomodatieSOLID"
**Requests:**
- **Architecture:** SOLID and three layers (UI / BLL / DAL), with repositories for the database and MVC for the frontend, following the example in [hetgameboekje/AccomodatieSOLID](https://github.com/hetgameboekje/AccomodatieSOLID).
- **WinForms**, with ext4 support through WSL.
- **Dictionaries** in a separate XML file, read with the DOM.
- **Secrets and the database connection** in a `.env` file. Use a public/private key pair where security is needed. No login, since this is a desktop app.
- **Swappable database:** the repository pattern must allow a switch from SQLite to SQL Server/MSSQL. The database is local, but enterprise use may need it on several computers.
- **Connections:** USB 3.0, SATA, NVMe, M.2 and other protocols where possible. Network sources aren't needed now; network data may be used later.
- **Network export, last and optional:** two processes, a backlog folder and an override folder, over SFTP and FTP.
- **Also:**
  - recommend Claude plugins
  - make a UI/UX that is simple but has depth
  - find GitHub code with a suitable license

**Done:** the restructure was built (Phase 1b). This plan was updated: sections 3, 7, 8 and 9 are new, and Phases 1b and 6–9 were added to section 5.

### 2026-11-12: "Add a WinDirStat-like view to the tool"
- Added as **Phase 2b: Disk map**, a treemap plus a folder tree of what takes the space, built from the catalog. See section 5.

### 2026-11-12: "Visualise the bad sectors of a disk"
- Added as **Phase 5b: Surface scan and bad-sector map**, with a block grid of read speed and errors, SMART data, and which files sit on damaged areas. See section 5.

### 2026-11-12: "Never run on crucial folders (AppData, Users, C:\ itself); free locked places after a failure; check for crucial points"
- Built: protected folders for every scan and destination, a run-state journal that frees what a crashed run held, and fixes from a risk review. See section 3.6 (what was built) and 3.7 (the review, including what is still open).

### 2026-11-12: "Make the unplug system and the rest; deleting always needs a 2-step verification"
- Built: two-step verification for every deletion (3.6), unplug handling for scan, hash and copy (3.8), and risk items 11–14 (3.7): catalog on a source disk is refused, the PC stays awake, reads have a deadline, long paths are reported.
- Interpretation of "2-step verification": (1) approve the exact list by its code, (2) confirm by typing `DELETE` (or a second dialog), then an automatic re-check of each file.

---

## 2. Current setup: PowerShell RecoveryToolkit (`file management` version)

**Files:**

| File | Purpose |
|---|---|
| `RecoveryToolkit.cmd` | Launcher: `powershell -NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File RecoveryToolkit.ps1` |
| `RecoveryToolkit.ps1` | The tool (~3,800 lines). WinForms GUI plus a background worker runspace (`$WorkerCode`). Starting it with `-BuildOnly` only builds the form, as a self-test. |
| `recoverscript.ps1` | The first, simpler command-line script (health check + PhotoRec + sort; admin only). |

**Structure of `RecoveryToolkit.ps1`:**
- **Embedded C#** (`Add-Type`):
  - `RT.Scanner`: the engine, with raw disk/partition reading, NTFS/FAT/exFAT file-table parsing for deleted files, and imaging with resume state.
  - `RT.Visual`: perceptual hashes for look-alike photos and videos (ffmpeg grabs 4 frames per video; grouping by duration).
  - Shell thumbnail interop and DPI awareness.
- **Worker** (runs in a runspace and talks to the GUI through a synchronized hashtable `$S`):
  - Logging: `G:\_RecoveryToolkit\log-<stamp>.txt` and a CSV report.
  - File categories: Photos / Videos / Audio / Documents / Archives / Other, by extension.
  - Filters: type, name patterns, include/exclude folders, min/max size, date range.
  - Duplicates: a size index, then SHA-256 only when sizes collide (`Add-Index`, `Find-Duplicate`).
  - Destinations: main + overflow list, a 1 GB reserve, FAT32 4 GB limit, "disk full" detection and switching to the next disk (`Pick-Dest`).
  - Resume: copies are written as `*.rtpart` and renamed when complete. A journal `resume-<hash>.tsv` lists finished files. PhotoRec's session is saved every 5 minutes in `_RecoveryToolkit\deep-<hash>\`.
  - Organize modes: Type, TypeDate (`Type\yyyy\yyyy-MM`), Keep (original folders), Flat.
- **GUI tabs:**
  1. **Collect / Recover**: from a folder, drive, whole disk or phone/camera (MTP), or a deep recovery (PhotoRec) on a disk or image. Preview, verify, report, overflow, and "Continue an interrupted run".
  2. **Quick scan (deleted)**: lists deleted files from NTFS/FAT/exFAT with names, sizes and an "intact" status, then undeletes the selected ones.
  3. **Disk tools**:
     - Health check (SMART, read speed) and chkdsk (check / fix / bad sectors).
     - TestDisk, bring online / clear read-only, and assign drive letters.
     - Sector-by-sector imaging (skips bad sectors, can resume) and restoring an image.
     - Linux partitions via WSL or 7-Zip.
  4. **Clean up duplicates**: exact duplicates or look-alike photos and videos. Previews per group, choose which copy to keep, and move the extras to `_Duplicates`, the Recycle Bin, or delete them.
- **External tools:**
  - PhotoRec: `C:\tools\testdisk\testdisk-7.2\photorec_win.exe`
  - ffmpeg: on PATH, in the script folder or in `C:\tools\ffmpeg`
  - 7-Zip: installed automatically if missing
- **Known limits:** one huge file; slow loops over millions of files in PowerShell; the GUI and threads are hard to extend; the duplicate index lives only in memory and isn't kept between runs or disks.

**Current disks** (2026-11-12):

| Disk | Model | Size | Letter | Role |
|---|---|---|---|---|
| 0 | Samsung SSD 850 EVO | 250 GB | D: "leeg" | – |
| 1 | Kingston SNV2S1000G | 1 TB | C: | Windows |
| 2 | Samsung MZVLB512 | 512 GB | E: | – |
| 3 | ASMT 2115 (USB) | 932 GB | F: "Folders" | Source of the first recovery |
| 4 | SanDisk Extreme 55AE (USB) | 1 TB | G: | Destination of the first recovery |

---

## 3. Decisions: language and architecture

### 3.1 Language
| Option | Verdict |
|---|---|
| PowerShell | At its limit: one 210 KB file, slow loops over millions of files, and the GUI and threads rely on workarounds. |
| C | No. Raw disk reads and hashing take a few lines in any language. C would cost months on the GUI and file handling for no benefit. |
| Delphi | Can do the job, but it's niche, has few libraries and is hard to get help with. |
| **C# / .NET 10 LTS (chosen)** | The existing embedded C# (`RT.Scanner`, `RT.Visual`) moves over almost as-is. It has fast parallel hashing, SQLite and SQL Server, the same Windows APIs, WinForms, and builds to a single `.exe`. |

External tools (PhotoRec, ffmpeg, untrunc, smartctl) do the heavy recovery work in any language. **Keep using the PowerShell toolkit** until each feature has been ported.

### 3.2 Architecture: 3 layers + SOLID (built 2026-11-12)
This follows the pattern of [AccomodatieSOLID](https://github.com/hetgameboekje/AccomodatieSOLID): the BLL owns the models, DTOs, **repository interfaces**, services and exceptions. The DAL references the BLL and **implements** those interfaces. The frontends wire the two together.

```
 RecoveryUI (WinForms, MVC)      RecoveryCli (rtcatalog.exe)        <- presentation: no logic, no SQL
        |  Views <-> Controllers <-> ViewModels   |  args -> services -> console
        v                                          v
 RecoveryBLL  (net10.0, platform-neutral)                            <- business logic
   Models, Dtos, Interfaces (IDiskRepository, IFileRepository, IReportRepository, ICatalogExchange,
   IVolumeProvider, IPhysicalDeviceProvider, IFileSystemWalker, IFileContentReader, IDictionaryRepository,
   IWslGateway, IOperationLog), Services (Scan, Fingerprint, Report, Merge, Disk, LinuxDisk, Dictionary,
   Catalog), Validation, Exceptions, Fingerprinting (ContentHasher, ImageHeaderReader)
        ^  implements
 RecoveryDAL  (net10.0-windows)                                      <- data access
   Sqlite/*      SqlServer/*      Xml/XmlDictionaryRepository (DOM)   Configuration/(.env)
   Storage/ WindowsVolumeProvider, WindowsPhysicalDeviceProvider (IOCTL), FileSystemWalker, WslGateway
 tests/RecoveryBLL.Tests (xUnit): 68 tests, including the full pipeline and the copy plan on SQLite and on SQL Server LocalDB
```

| SOLID | Where |
|---|---|
| **S**ingle responsibility | One service per job (scan / fingerprint / report / merge / disks / Linux), one repository per table area, one controller per page. |
| **O**pen/closed | A new database means new repository classes plus one `case` in `RecoveryDAL.ServiceRegistration`. New file types, skip folders and bus types are XML edits, not code. |
| **L**iskov | SQLite and SQL Server repositories pass the same tests and give the same report (verified: 135.77 GB unique and 93.86 GB extra on both). |
| **I**nterface segregation | Repositories are split: disks, files, report, exchange and store. Services depend only on what they use. |
| **D**ependency inversion | The BLL defines the interfaces and the DAL implements them. Composition (DI) happens only in `Program.cs` of each frontend. |

- **MVC in WinForms:**
  - Views (`HomeView`, `DisksView`, `ScanView`, `ReportView`, `SettingsView` and `MainForm`) implement interfaces (`IScanView` …), raise events and never call services.
  - Controllers call the BLL on a worker thread through one shared `OperationRunner`, so two scans can never overlap.
  - ViewModels format everything for display. `[ExpertOnly]` columns appear only in Expert mode.
  - All views have `.Designer.cs` files, so the Visual Studio designer opens them.

### 3.3 Database providers (repository pattern)
| Provider | Status | Use |
|---|---|---|
| **SQLite** (default) | Done | One local file, nothing to install, and existing `catalog.db` files keep working. |
| **SQL Server / LocalDB / Express** | Done and tested on LocalDB | `DB_PROVIDER=sqlserver`. The database and schema are created automatically. For several PCs, all scan into one server. |
| PostgreSQL (option) | Later | `Npgsql` (PostgreSQL license). Same interfaces; only `RecoveryDAL/Postgres/*` would be new. |

- A cross-provider merge works: a SQLite catalog from another PC merges into SQL Server (28,772 records in about 4 s).
- SQL Server specifics:
  - `MERGE` with `SqlBulkCopy` staging tables, for speed
  - a persisted SHA-256 `path_key`, because long paths can't be a unique index
  - `VARBINARY(16)` hashes

### 3.4 Configuration, secrets and keys
- **`.env`** (never committed; `.env.example` is):
  - `DB_PROVIDER`, `DB_CONNECTION`, `DB_PASSWORD`, `DICTIONARIES_FILE`, `LOG_DIR`, and `NET_*` for the network export
  - `${VAR}` expansion, so a password lives in one variable
  - real environment variables override the file
  - the UI and logs only show a password-free description
- **`config/dictionaries.xml`**, loaded with the **XML DOM** (`XmlDocument` + XPath). DTDs and external entities are disabled, so the file can't pull in outside content (there's a test for this). It holds:
  - file categories, skip folders, partial-file extensions
  - bus types with recovery notes and reader counts
  - junk/preview rules for Phase 3/3b
  - "Reload" in Settings reads it again
- **Keys:** there's no login (desktop app). Public/private keys are used where something leaves the PC:
  - **SFTP:** ed25519 key authentication (`NET_PRIVATE_KEY`, optional passphrase) plus host-key **pinning** (`NET_HOST_KEY_FINGERPRINT`), so an upload refuses a changed server.
  - **SQL Server on another machine:** Windows authentication, or a SQL login whose password stays in `.env`, with `Encrypt=True` (TLS).
  - Optional later: encrypt `.env` secrets at rest with Windows DPAPI (per user, no key file to manage).

### 3.5 UI/UX: simple first, depth on request
- **Simple mode (default):**
  - **Home** shows where you are (disks, files, unique data, extra copies, not yet decided) and **one "Next step" sentence**, plus 3 numbered step cards.
  - **Catalog disks** has two steps (tick drives or folders, press Start), a progress bar with time left, and "Read-only: nothing is changed" in plain words.
  - **Report** shows 4 tiles, one sentence ("One copy of everything needs 135.77 GB"), and tables per type and per disk.
- **Expert mode (toggle, remembered):**
  - disk number, model, bus, HDD/SSD, volume serial, readers per disk, TRIM warnings
  - physical disks, and Linux (WSL) mounting
  - readers per disk, SHA-256 and exclude options
  - largest duplicate groups with every path
  - the live log
- **Disk map** (Phase 2b): a WinDirStat-style overview of where the space goes. Simple mode shows the map and a legend; Expert mode adds the folder tree, the extension list and the duplicate overlay.
- **Disk health** (Phase 5b): a grid of coloured blocks showing how fast each part of the disk reads and where it fails, with a plain verdict on top ("Healthy" / "Weak spots" / "Failing: copy your data first").
- **Coming next** (planned phases) is visible but greyed out in the sidebar, with a tooltip per phase.

### 3.6 Safety: protected folders and crash recovery (built 2026-11-12)
**Protected folders** (`PathSafetyValidator` in the BLL, the list from `WindowsProtectedLocations` in the DAL). Checked for every scan source and every copy destination, in the CLI and the desktop app:
- **The Windows drive as a whole is refused** (`C:\`, also typed as `C:`). Only a subfolder may be used, e.g. `C:\Recovery` or `C:\Users\<name>\Pictures`.
- **Never used, nor anything inside them:** Windows, Program Files (both), ProgramData, **every user's AppData**, Default/All Users profiles, Recovery, System Volume Information, `$Recycle.Bin`, `$WinREAgent`, `$Windows.~BT/~WS`, `$SysReset`, Config.Msi, Boot.
- **A folder that contains one of those is refused as a whole:** `C:\Users`, `C:\Users\<name>`. Their subfolders (Pictures, Documents, Desktop…) are fine.
- Other drives (`D:\`, `H:\`) may be used whole. Network paths are refused (not supported yet).
- Paths are normalised first, so tricks don't get past it: `C:\PROGRA~1` (8.3 names), the `C:\Documents and Settings` junction, `..`, `\\?\` prefixes.
- The list is built from the real system (Windows folder, profile locations), not hard-coded to `C:`.
- WSL mounts (`\\wsl.localhost\...`) are exempt; they carry their own identity and are read-only.

**Keeping Windows usable while copying:**
- A destination on the Windows drive keeps **at least 20 GB free** (`ConsolidateOptions.SystemDriveMinFree`) instead of 1 GB, and the plan warns that a separate disk is safer.
- `plan` catalogs **only the destination folder**, not its whole volume (before, `C:\Recovery` would have meant scanning all of C:).

**Freeing what a failed run held** (`RunStateService` + journal `run-state.json` next to the logs):
- Before a run takes something, it writes it to the journal with its process id and start time:
  - **WSL mounts** (`wsl --mount`): if the run dies, the Linux disk stays attached and Windows can't use it.
  - **Copy destinations**: a killed copy leaves `*.rtpart` files behind.
- **At every start** (rtcatalog and the desktop app), entries whose process is gone are freed: the Linux disk is unmounted, and leftover `*.rtpart` files are **listed, not deleted** (deleting needs the two steps below). Unmounting needs administrator rights. Without them, the entry stays in the journal and the exact `wsl --unmount` command is shown. A destination stays in the journal as a reminder until its leftovers are gone.
- **`rtcatalog cleanup [folder ...]`** shows the same on request, shows what live runs still hold, and can list leftover `*.rtpart` files in a folder the journal doesn't know. A live run's `.rtpart` files are never offered.
- **Two runs can't write the same destination** (e.g. the app and rtcatalog at once). The second one is refused with the process id of the first.
- Robustness:
  - A lock file serialises the journal (deleted when its process ends).
  - The JSON is replaced atomically.
  - A damaged journal is set aside instead of blocking the toolkit.
  - A reused process id is detected through the start time.

**Every deletion needs two-step verification** (built 2026-11-12; `DeletionBatch`, `IDeletionConfirmer`):
1. **Approve the exact list.** `plan` (old duplicates to make room) and `cleanup` (leftover `*.rtpart`) show what would be deleted, why, and a 6-character **approval code** computed from those paths and sizes. The deletion runs only with `--approve-delete <code>`. If anything in the list changed (another file, another size), the code no longer matches and nothing is deleted.
2. **Final confirmation, right before deleting.** In the console you have to type `DELETE`. Input from a script or pipe is refused, so it always needs a person at the keyboard. In the desktop app there are two separate dialogs, both defaulting to "No".
3. Then the toolkit **re-checks each file** just before removing it. Old duplicates: the file is unchanged and its kept copy is still there with the same size. Leftovers: still a `.rtpart`, unchanged, and no live run writing there.
- Without approval nothing is deleted, and the copy simply goes on: files that needed that room wait for the next run.
- Without a frontend confirmer (e.g. a new tool), the default `RefuseAllDeletions` deletes nothing.
- One exception, by design: a `.rtpart` that the **same run just wrote** is removed when that copy fails or is stopped. It is the run's own half-file, never user data.

### 3.7 Risk review (2026-11-12)
**Fixed in this round:**

| # | Risk | What could happen | Fix |
|---|---|---|---|
| 1 | Scan or destination on `C:\`, AppData, Windows, Program Files | Scanning reads every locked system file and stalls the PC; writing there can break programs or Windows | 3.6 protected folders |
| 2 | `plan` scanned the whole destination volume | A destination such as `C:\Recovery` meant cataloguing all of C: | Only the destination folder is scanned |
| 3 | Copy fills the Windows drive | Windows can't update, page or write temp files and becomes unusable | 20 GB minimum free on the system drive |
| 4 | WSL mount left attached after a crash or kill | The Linux disk stays locked by WSL until it is unmounted by hand | Journal + automatic unmount at start |
| 5 | `*.rtpart` files left after a kill or power loss | Wasted space, confusing half-files | Listed at the next start; deleted with the two-step check (`cleanup`) |
| 6 | Two runs writing one destination | Both pick the same names; the space calculations are wrong | Destination lease; the second run is refused |
| 7 | Eviction while the kept copy was removed between plan and copy | The last copy of a file would be deleted | Kept copy re-checked (exists, same size) right before deleting |
| 8 | Disk unplugged during scan, hash or copy | Copy: an error per remaining file. Hash: every remaining file marked missing/unreadable for good | 3.8: wait and continue (copy), stop that disk without touching its files (hash) |
| 9 | exFAT/FAT destinations round modified times | The next `plan` sees every copied file as "changed" and re-reads all of it | The catalog records the time as the destination stores it |
| 10 | A deletion runs without a person seeing it | Data loss through a wrong plan or a script | Two-step verification for every deletion (3.6) |
| 11 | **Catalog on a source disk** | Writing `catalog.db`, logs or the journal to a disk being recovered can overwrite deleted files that could still be undeleted | Scanning that disk is **refused**, with how to move the catalog (`DB_CONNECTION`/`LOG_DIR`/`--db`). On the Windows drive (written to all the time anyway) it is a warning not to recover *deleted* files from it. |
| 12 | **Sleep during a long run** (it happened once, section 1) | The run stops halfway | Scan, hash and copy keep the PC awake (`SetThreadExecutionState`; the screen may still turn off) |
| 13 | **Failing source disk:** a read on a bad sector hangs for minutes | The run hangs | Every read has a 60 s deadline. The file then gets a "read timeout" error. After 3 timeouts on one disk, that disk is skipped with the advice to image it first |
| 14 | **Paths over 260 characters** | Explorer and older tools can't open the copy | Copying them works (tested). `plan` warns with the count |

**Still open, most important first:**
1. **Windows Update restarts** can't be blocked by an app. Runs continue safely after one (resume, journal, `.rtpart`), but pause updates before long runs (section 6). Possible later: warn at the start of a copy when a restart is pending.
2. **Shared SQL Server catalog on several PCs** (Phase 8/9). Not reachable today: destinations must be local disks (network paths are refused), so two PCs can't write the same destination. When network destinations come (Phase 9), the destination lease must move into the shared catalog or the destination itself, with a heartbeat.
3. **A failing disk can still hang inside the driver.** The 60 s deadline cancels the read (`CancelIoEx`), but some USB bridges only give up after their own retries. Imaging first stays the advice for disks with SMART warnings (Phase 5b).
4. **Memory:** the plan holds the whole catalog in memory (about 200–300 MB per million files). Fine for now; could stream per disk later.
5. **Destination on the Windows drive:** anything catalogued earlier elsewhere on C: counts as "already there" and isn't copied into the destination. That's correct for a backup *on* C:, but another reason to use separate backup disks.
6. **Not yet tested on real hardware:** pulling a real USB disk during a copy, a real bad-sector timeout, and the refusal for a catalog on a non-Windows source disk (the tests simulate these).

### 3.8 Unplugged disks (built 2026-11-12)
Disks are recognised by their identity (volume serial + size), never by drive letter, so a disk may come back on any port under another letter.

| When | Unplugged disk | What happens |
|---|---|---|
| Scan | the disk being scanned | What was listed is kept. Nothing is marked as deleted (an incomplete walk never marks files missing). Scan again later. |
| Fingerprint | a source disk | That disk stops; **its files keep their state**. Before, every remaining file would have been marked "missing" or "unreadable" and skipped for good. Other disks continue; `hash` finishes it later. |
| Copy | a source or destination disk | The copy **pauses and waits** (default 30 minutes; `--no-wait` skips the disk instead) with a message to plug it back in. When it is back, under any letter, the interrupted file is copied again and the run continues. If it doesn't come back, its files wait for the next run. |
| Copy | a destination comes back with another letter | The new letter is used for the rest of the run. |
| End of a copy | – | "Safe to unplug": every copy was flushed to the disk (`Flush(true)`) before it was renamed, so nothing sits in a cache. |
| Crash while a Linux disk is mounted | – | Unmounted at the next start (3.6). |

---

## 4. Core idea: catalog first, copy later

Nothing is copied until **every** disk has been read and fingerprinted. You then know the real amount of unique data, and whether it fits on 2 TB + 1 TB, **before** writing anything. This prevents most overflow.

---

## 5. Plan

### Phase 0: finish the current job
- Let the leftover sort on G: finish, then check the summary in `log-20261112-111844.txt`.
  _Status 2026-11-12 12:19: still running, 9,603 files (~181 GB) left in the raw folder._
- Delete the old `Desktop\data` copy so it can't be started by mistake. _(Waiting for confirmation.)_

### Phase 1: Catalog (fingerprint database), read-only
> **Built 2026-11-12** as `rtcatalog.exe` (now `src/RecoveryCli` on top of the BLL/DAL; see Phase 1b) (.NET 10, the current LTS; usage in README.md).
> Tested on generated test data and on E: (28,772 files, 230 GB): listing took 1.7 s and a full fingerprint took 1 min 40 s with 4 threads.
> E: turned out to hold 93.9 GB of extra copies (135.8 GB unique).
> Also stores image width/height and EXIF camera/date now, for Phase 3/3b. Video duration (ffprobe) isn't done yet.
- **SQLite database file** (CSV is too slow at millions of rows).
- **Hash:** xxHash128 for speed (SHA-256 optional). Note: this is a content hash, not base64.
- **Table `files`:** disk ID (volume serial + label), path, size, modified date, quick hash, full hash, media type, width/height/duration, opens OK (yes/no), first seen.
- **Indexes:** `(size, quick_hash)` and `(full_hash)`. Small and fast, as requested.
- **Funnel, to minimise reading:**
  1. A file with a unique size can never be a duplicate, so it's not read in full.
  2. Files that share a size get a quick hash (first + last 64 KB).
  3. Only files that still match get a full hash.
- **Incremental:** a re-scan skips files whose path, size and date haven't changed, so a scan can stop and continue at any time.
- Scan every disk: the controller disk, the HDDs, and the NVMe/SATA disks in the other PC. For the other PC, run the app there and **merge the database file** afterwards; there's no need to remove the disks.
- **Output:** a report per disk (total, unique, duplicates).

### Phase 1b: restructure to 3 layers + SOLID (done 2026-11-12)
- `RecoveryBLL`, `RecoveryDAL`, `RecoveryCli` (`rtcatalog.exe`), `RecoveryUI` (`RecoveryToolkit.exe`, WinForms MVC) and `tests/RecoveryBLL.Tests`. See section 3.
- The same behaviour as the Phase 1 tool. The CLI adds `devices` (physical disks and bus) and `wsl status|scan`.
- `--threads auto` is now the default: 1 reader for an HDD, more for an SSD/NVMe, based on the bus.

### Phase 2: Consolidate (copy plan)
> **Built 2026-11-12** (BLL/DAL/CLI): `rtcatalog plan <main> [overflow ...]` (dry run) and `rtcatalog copy ...`.
> `ConsolidationPlanner` (pure, from the catalog only) + `ConsolidationService` (catalogs the destinations, fingerprints what is open, copies).
> Tested on SQLite and SQL Server LocalDB.
> **Desktop app "Copy" page built 2026-11-12** (`CopyView` + `CopyController`; replaces the greyed "Copy plan" entry; Home step 3 opens it):
> - **1 Destinations:** pick a drive or type/paste a folder path, or browse. The order is the priority (the first is "Main", the rest "Overflow"), with Move up/down. Expert mode: a size cap per destination.
> - **2 Check the plan** (dry run): four tiles (will be copied / unique / duplicates / already there), one verdict sentence (fits / does not fit / nothing left), a destinations table and notes (pending files, unplugged sources, same-disk files, Windows drive, long paths). Expert mode: options (duplicates, verify, wait for unplugged disks, allow pending, space kept free) and "Save the full list (CSV)". The CSV writer is shared with the CLI (`PlanExport`).
> - **Deletions in the app:** "Review and approve the deletions…" is step 1 (shows the exact list). Step 2 is a second dialog that the copy shows right before deleting (on the UI thread). Changing a destination or option discards the plan and the approval.
> - **3 Copy:** a confirmation with the destinations, then progress, Stop, and a result summary (copied, removed, not approved, waiting for a disk, no room, errors, failing disks). The plan is cleared afterwards; checking again shows what is left.
> - Checked by launching the app with a demo catalog and walking through it with UI Automation (screenshots). Not yet done in the app with real disks: a full copy and the step-2 dialog.
> - Found while testing: a destination folder on the same disk as the sources gave "nothing to copy" without explanation. The plan now says how many files are on the destination's own disk and why they are not copied again.
>
> How the details were settled:
> - **Layout:** main = `<Category>\<original path>` (the category is not repeated when the path already starts with it);
>   `_OlderVersions\<disk>\<original path>` and `_Duplicates\<disk>\<original path>`, where `<disk>` = label_serial.
> - **"Same name"** means the same folder + name (the same main path), not just the same file name, so two different `IMG_0001.JPG`s from different folders are both kept normally.
> - **Which identical copy is kept:** the oldest modified time (closest to the original), then the shortest path.
> - **"Already there":** the content (full hash) is anywhere on a destination disk. Each destination volume is catalogued completely by `plan`.
> - **Destination already holds the main path with other content:** if ours is older it goes to `_OlderVersions`; if newer, it is kept next to it as `name (2).ext`. Nothing on a destination is overwritten.
> - **Space:** destinations are filled in the given order (1 GB reserve each, `=size` caps one, FAT32 4 GB limit). Unique files first; duplicates only in what is left.
> - **Eviction:** old `_Duplicates` files are removed (oldest first) only for a unique file that fits nowhere else, and only when the same content is kept on a destination outside `_Duplicates`.
> - **Not fingerprinted yet:** `copy` refuses (pending files can't be told apart from duplicates) unless `--allow-pending`.
> - **Resume:** copies go to `name.rtpart`, are checked against the catalog hash (optionally read back with `--verify`), keep their modified time, then are renamed and recorded in the catalog. A re-run sees them as "already there". Files on unplugged source disks wait for the next run.

Each file gets one of four labels:
1. **Unique** → main folders (Photos/Videos/…) on the backup disk, then the overflow disk.
2. **100% identical to a file already kept** → `_Duplicates` folder. Switch: move there / skip entirely.
3. **Same name, different content** (stale manual copies) → the **newest** goes to main and older ones go to `_OlderVersions`. Never deleted automatically.
4. **Already on the destination** → nothing to do.

**`_Duplicates` rule (on/off):** it is the lowest priority. Only when both the backup and overflow disks are full are duplicates deleted (oldest first) to make room for unique files. A unique file is never deleted to make room. _(Confirmed 2026-11-12.)_

**Dry-run report first**, e.g. "1.31 TB unique, 820 GB duplicates, 4,200 older versions → fits on 2 TB."

Resume safety stays the same: `.rtpart` files, a journal, and pausing when everything is full.

### Phase 2b: Disk map (WinDirStat-style overview)
> **Built 2026-11-12** as the "Disk map" page in the desktop app (between Report and Copy).
> - **BLL:** `DiskMapService` builds a `MapNode` tree (disk → folders → files) from `IReportRepository.ReadMapFiles`, a streamed query that uses the same classification as the report. Each file is unique, kept (the counted copy of a duplicate group), extra copy, or pending. `TreemapLayout` is a pure squarified layout with cushion coefficients and hit testing.
> - **Big catalogs:** files smaller than 1/250,000 of the map are merged into one "N small files" block per folder, and blocks under 2 px are not split further. The folder tree fills in only when a folder is expanded (at most 300 children; the rest stay on the map).
> - **Drawing:** `TreemapControl` draws cushion shading per pixel into a bitmap (light from the top left), stripes for extra copies, a dotted edge for pending, grey for everything except a highlighted extension, and an outline for the selection. It is laid out again 150 ms after a resize.
> - **Page:** "Show:" (all disks together, or one disk); a summary sentence; a legend with sizes (names always written, so colour is never the only cue); "Showing: …" with Zoom out; hover text (path, size, type, copy state, date). Double-click zooms in. Right-click offers **Show in Explorer** (only when the disk is plugged in and the file still exists), Copy path, and Zoom in/out. Expert mode adds the folder tree and the extension list, linked both ways with the map, and Save as PNG.
> - **Colours:** a `color` attribute per `<category>` and `defaultColor` on the dictionary in `dictionaries.xml`, validated as `#RRGGBB`.
> - The map is rebuilt after a scan or copy finishes (marked stale), or with Refresh.
> - **Tests:** layout areas are proportional with no overlap and near-square blocks; folders too small to split stay one block; hit testing works; the map from a real scan has the right sizes, types, colours, extra copies and extensions (SQLite and SQL Server); tiny files are merged.
> - **Checked on a snapshot of the real catalog** (F: + G:, 129,854 non-empty files, 502.72 GB): the map was ready within the 12 s the screenshot script waited (not timed more precisely). 113.79 GB are extra copies; videos take 394 GB, of which 71.9 GB are extra copies. The biggest striped area is `G:\From_E\MediaRecovery\_Duplicates` (the old toolkit's duplicates folder): 13,902 files, 100.8 GB, all of it extra copies (checked with SQL on the snapshot).
> - **Not built yet:** overlays for junk and previews (they need Phase 3/3b).
A visual answer to "what takes the space?", in the same spirit as [WinDirStat](https://github.com/windirstat/windirstat). It's our own implementation: WinDirStat is GPL-3.0, so its code can't be copied into this MIT project; only the idea is reused.

- **Layout:** a new "Disk map" page with three linked parts:
  1. **Folder tree** (top left): folders sorted by size, with size, % of the parent, file count and last change. Expanding a folder shows its subfolders and files.
  2. **Extension list** (top right): extension, colour, total size, % and file count, sorted by size. Clicking an extension highlights all its files in the map.
  3. **Treemap** (bottom): every file is a rectangle sized by its bytes, grouped by folder and coloured by file type (Photos / Videos / Audio / Documents / Archives / Other, the categories from `dictionaries.xml`). It has shaded "cushion" tiles, so folder borders stay visible.
- **Linked selection:** clicking a rectangle selects the file in the tree, and selecting a folder outlines its area in the map. Hovering shows the path, size and date. A right-click menu offers "Open in Explorer" and "Copy path" (read-only; no delete from this view).
- **Scope selector:** one disk, one folder, or **all catalogued disks together**, with one top-level block per disk.
- **Recovery-specific overlays** (the reason to build this rather than run WinDirStat):
  - **Duplicates:** extra copies drawn hatched/dimmed, so "this folder is 40 GB, of which 25 GB are copies" is visible at a glance.
  - **Not yet decided:** files still waiting for fingerprinting get a dotted outline.
  - **Junk and previews** (after Phase 3/3b): thumbnails, icons, skins and game assets marked, to show how much space they take.
  - **Unplugged disks** still show, because the map is built from the catalog, not from the disk.
- **Data source:** only the catalog database. No disk is read again, so the map opens instantly even for unplugged disks. A new repository method returns the folder sizes per disk (`IReportRepository.GetFolderTree(diskId, prefix)`), aggregated in SQL; both SQLite and SQL Server get it.
- **Drawing:**
  - our own squarified treemap algorithm (Bruls, Huizing & van Wijk, 2000; a published algorithm, free to implement)
  - drawn in a custom WinForms control with GDI+, double-buffered
  - the layout is computed off the UI thread and cached per scope
  - large catalogs: rectangles smaller than 1–2 px are merged into their folder's block, so millions of files stay smooth
- **Colours:** one colour per category from `dictionaries.xml` (a `color` attribute added to `<category>`), editable like the other dictionaries. Text and outlines meet contrast rules, and the legend always shows the names, so colour is never the only cue.
- **Simple vs Expert:**
  - **Simple:** map plus legend, and a one-line summary ("Largest: Videos 116 GB, of which 72 GB are copies").
  - **Expert:** the full folder tree, the extension list, overlays per type, and exporting the map as PNG.
- **Order:** after Phase 2, because the copy plan comes first. It can start any time after Phase 1b, since it only needs the catalog.

### Phase 3: Junk detection (moved to `_Review\Junk`, never deleted)
> **Built 2026-11-12.** Two changes from the first idea, both based on the real catalog:
> 1. **Nothing is moved on the source disks** (they stay read-only). The rules only decide where **Copy** puts a file: junk goes to `_Review\<reason>\<disk>\<path>` on the backup disk, after your own files and only into space that is left (`--junk skip` leaves it out; `off` switches detection off).
> 2. **Keep-together folders were added.** F: holds two backups of a Minecraft server (52.4 GB, 87,831 files: world `.mca` regions, mods `.jar`, 77,000 BlueMap tiles), a Postgres `postgres-data` folder and three Battle.net installs. Sorting these by file type would have scattered textures into Photos and jars into Other, and removing duplicates inside them would have broken the second backup. They are now copied **whole**, in their own structure and without deduplication:
>    - to `Apps and data\<disk>\<path>` when worth keeping (Minecraft, Database, Code project);
>    - to `_Review\<name>\…` when reinstallable (Programs = a folder with an .exe and 3+ .dll files, Games = steamapps, App data, Program Files).
>
>    A marker in a disk root is ignored, so a whole disk never becomes one folder. The outermost match wins.
>
> - **Code:**
>   - `FileTagger` holds the pure rules;
>   - `JunkService` runs them on the catalog, read with `IReportRepository.ReadFileFacts` (path, extension, size, type, width/height, camera yes/no), on SQLite and SQL Server;
>   - `ConsolidationPlanner` has the new actions `KeepTogether`, `Review` and `SkippedReview`. Of identical copies, a non-junk copy is preferred as the keeper, and junk extra copies are not copied. The copy order is own files, then `_Review`, then `_Duplicates`.
> - **Rules** (`dictionaries.xml`): `scrap` sizes per category, `junkFile`, `junkFolder`, `assetFolder`, and the new `keepTogether` dictionary (`marker` / `folder` / `program`, action `keep` or `review`). They are validated on load.
> - **Junk order:**
>   1. System files (`Thumbs.db`, `desktop.ini`…)
>   2. Caches (`.thumbnails`, `cache2`, `INetCache`…)
>   3. Scraps: video < 16 KB, audio < 4 KB, picture < 200 B
>   4. Icons and skins: `.ico`; PNG 64×64/64×32; square 16–256 px PNG/GIF/BMP without camera data
>   5. Thumbnails: known sizes without camera data
>   6. Image sets: 200+ same-size images ≤ 256 px in one folder
>
>   Camera data always protects a photo, and so does a JPEG that happens to be square.
> - **Real catalog result** (`rtcatalog junk`, about 3 s; F: + G:): 3,950 junk files (21.7 MB: 2,790 thumbnails, 716 icons and skins, 438 scraps including the 24-byte "mp4" files, 6 `desktop.ini`); 3 folders kept whole (52.7 GB); 3 reinstallable programs (859 MB).
> - **Desktop app:**
>   - The "Junk and apps" page (it replaces the greyed "Clean up junk"): three tiles; what was found, with where it goes; the folders kept whole; the 200 largest files per junk reason, with right-click Show in Explorer / Copy path. "Check again" reloads the rules. A shared `ExplorerLauncher` is now also used by the disk map.
>   - The Copy page: a "Junk and programs" tile, a junk option (Expert), and two new lines in "What does Copy do?".
> - **Tests:** rules (bundles, outermost wins, root marker ignored, programs, every junk reason, camera protection, switching off); copy (two identical server backups both copied whole, junk to `_Review`, junk extra copy skipped, rerun finds everything, junk only gets leftover space, skip/off).
> - **Not built yet:** a junk/keep-together overlay on the disk map; per-folder "this is not junk" exceptions in the app (for now: edit the rules).
- **Minecraft skins:** PNGs of exactly 64×64 or 64×32.
- **Program icons and pictograms:** square images of 16/24/32/48/128/256 px, and `.ico` files.
- **Game assets:** source paths with `.minecraft`, `resourcepacks`, `steamapps`, `AppData`, `Program Files`, or folders with hundreds of tiny same-size images.
- **Thumbnails and cache images:** below a size or dimension limit.
- Files recovered by PhotoRec have no folder names, so only the image-size rules work for them.

### Phase 3b: Low-quality previews and thumbnails
**Why there are so many:** PhotoRec recovers every JPEG it finds on the disk surface, not just real photos. That includes:
- the small thumbnail stored inside each photo's EXIF data (~160×120)
- the preview JPEG inside RAW files (~1620×1080)
- Windows `thumbcache` entries
- phone and gallery app cache images
- website and app cache images

So one real photo can come back as 2–3 extra small copies.

**Detection.** Each picture gets a score; the threshold can be adjusted, with a preview before anything happens.
- **Size:** longest side ≤ 640 px (adjustable; e.g. 160/256/320 are typical thumbnail sizes), and a small file size for the resolution.
- **No camera data:** missing EXIF make/model/date usually means a generated thumbnail, not an original.
- **Known thumbnail sizes:** 160×120, 120×160, 256×256, 320×240, 96×96, and so on.
- **"Has a full version":** the existing look-alike matching (`RT.Visual` perceptual hash) finds the full-resolution photo it was made from. This is the strongest signal.

**Two groups, each with its own default action:**

| Group | Meaning | Default | Options |
|---|---|---|---|
| **Preview of an existing photo** | A full-size version exists in the catalog | Move to `_Previews\HasOriginal` | Move / Delete |
| **Orphan preview** | No full version found; this may be the only copy of that memory | Move to `_Previews\Orphan` | Move / Delete / **Upscale** |

**Upscale option:**
- Tool: **Real-ESRGAN** (`realesrgan-ncnn-vulkan.exe`). It's free and portable, runs on the GPU, and is called as an external process like ffmpeg, with a timeout.
- 2× or 4×, with a photo model (or the anime model for drawings).
- The original small file is **always kept** (in `_Previews\Orphan`).
- The upscaled result is saved as `<name>_upscaled.jpg` in `Photos\_Upscaled` and is marked as upscaled, so it's never mistaken for an original.
- **Expectation:** upscaling makes small pictures look smoother and sharper, but it can't bring back detail that isn't there. A 160 px thumbnail stays a guess. It works best on images of ~400 px and up.
- It's resumable through the same journal as the repair queue, so a crash skips finished files.

All of this runs on the catalog (Phase 1) data, so detection needs no extra reading of the disks beyond decoding the image dimensions once.

### Phase 4: "Corrupt files" tab
- **Scan a folder:**
  - Videos and audio: ffprobe, plus a short test decode at several points in the file.
  - Images: a full decode.
  - PDF/Office files: a structure check.
  - Result: OK / Corrupt / Truncated / Unknown.
- **Safe from the start:** right after the scan, corrupt files are moved to `<folder>\_Corrupt\`. A journal stores the original path of each file.
- **Actions:** Delete / Move to subfolder / Recover.
- **Recovery queue** (state saved per file):
  - **Success:** the repaired file is checked again, then moved back to its original folder.
  - **Unrecoverable:** moved to `_Corrupt\Unrecoverable`.
  - **Script quits or crashes:** on the next start, half-written output is removed and the file is retried. After 2 failed attempts it's marked "crashes the repair tool" and is never retried in a loop.
- **Repair methods:**
  - ffmpeg remux (`-c copy`, ignore errors) for broken containers.
  - **untrunc** for MP4/MOV files without an index. It needs one healthy video from the same camera or phone; this is picked automatically from the catalog.
  - JPEG: keep the readable part. PNG: fix the checksums.
- Each repair runs as a **separate process with a timeout**, so a hang can't freeze the app.
- **Expectation:** many of the 3,163 videos PhotoRec found are corrupt because they were **fragmented** on the disk. PhotoRec can't put fragments back together, so most of these will end up as unrecoverable. The scan will at least show how much space they take.

### Phase 5: port the remaining PowerShell features
Deep recovery (PhotoRec), quick scan/undelete, disk tools, imaging and look-alike duplicates are ported to the C# app. After that, the PowerShell toolkit is retired.

### Phase 5b: Surface scan and bad-sector map
See at a glance where a disk is slow or damaged, and **which files are affected**, in the spirit of tools like Victoria or HD Tune. It's our own implementation.

- **The view** (new "Disk health" page, or a tab on Disks):
  - **Block grid:** the whole disk as a grid of small squares. Each square is one block of LBAs (e.g. 1,024 blocks across the surface, adjustable).
  - **Colour by read time:** fast (green), slower (yellow), very slow (orange, often a sector the drive is struggling with), unreadable (red), not tested yet (grey).
  - **Speed graph:** read speed (MB/s) over the disk position. A healthy HDD shows a smooth line that drops from the outer to the inner tracks; dips and spikes stand out.
  - **Summary** in plain words: blocks tested, slow and unreadable counts, a time estimate, and a verdict: "Healthy" / "Weak spots" / "Failing: copy your data first".
  - **Hover / click a block:** its LBA range, read time, error, and the **files stored there** (see below).
- **SMART panel** next to the grid. These attributes predict failure, so they're shown with explanations:
  - 5 Reallocated sectors, 197 Pending sectors, 198 Offline uncorrectable, 199 CRC errors (often a cable or USB bridge, not the disk), 9 Power-on hours, 194 Temperature.
  - Source: `smartctl` (smartmontools, external, GPL), with `-d sat` for USB-SATA bridges such as the ASMT 2115. NVMe uses its own health log (media errors, percentage used).
- **How the scan works (read-only, gentle on a failing disk):**
  - It reads `\\.\PhysicalDriveN` directly, which needs **administrator** rights. Large sequential reads per block, timed with a high-resolution clock.
  - On an error or timeout it **skips ahead** and doesn't retry in a loop, like ddrescue's first pass. A dying disk gets the fewest possible extra reads.
  - **Safety stop:** after N unreadable blocks or a sudden SMART change (pending sectors rising), it pauses and advises **imaging first** (Phase 5 imaging), then working from the image.
  - It never writes. There's no "remap"/"erase" mode; that belongs in vendor tools, not a recovery tool.
  - Stop and resume like the catalog: progress is saved per block.
- **One map for scan and imaging:** the Phase 5 sector imaging (bad-sector skip) writes the **same block map**. Imaging a failing disk therefore produces the bad-sector picture for free, and both show in the same view.
- **Files on bad sectors** (the recovery-specific part):
  - **NTFS:** the file allocation is read from the MFT through **DiscUtils** (MIT) or `FSCTL_GET_RETRIEVAL_POINTERS`. Every catalogued file overlapping an unreadable or very slow block is flagged.
  - Results go into the catalog (e.g. `files.on_bad_sector`) and feed:
    - **Phase 4 (Corrupt files):** these files are checked first.
    - **Phase 2 (copy plan):** if another copy exists on a healthy disk, that one is used.
    - **Phase 2b (Disk map):** an overlay that outlines affected files in red.
  - FAT/exFAT come through DiscUtils too. ext4 is later (Phase 6).
- **Storage** (both providers): a `surface_scans` table (disk, date, block size, verdict, SMART snapshot) and `surface_blocks` (scan, block index, read ms, status), compressed into runs of equal blocks so a 5 TB disk stays small.
- **Per connection type** (from Phase 7):
  - **HDD:** the most useful case, because read time shows weak areas well.
  - **SSD/NVMe:** read time says little (wear-levelling hides the physical layout). The view shows errors plus the NVMe/SMART wear values instead, with a note.
  - **USB bridges:** some hide or delay errors. A "bridge may hide errors" note appears when the bus is USB and SMART isn't passed through.
- **Simple vs Expert:**
  - **Simple:** verdict, grid and a "What should I do?" line.
  - **Expert:** block size, timeout, thresholds per colour, raw SMART table, LBA details, and export (PNG of the grid, CSV of the blocks).
- **Order:** with the Phase 5 port of disk tools and imaging, since they share the raw-disk reader and the block map.

### Phase 6: Linux file systems (ext4, btrfs, xfs)
- **Done (basic):** Expert mode → Disks → "Linux disks via WSL", or `rtcatalog wsl scan <disk#> <partition#> ext4`.
  - It runs `wsl --mount \\.\PHYSICALDRIVEn --partition p --type ext4 --options ro` (read-only), scans `\\wsl.localhost\<distro>\mnt\wsl\PHYSICALDRIVEnpP`, and unmounts.
  - The catalog identity is the filesystem **UUID** and size (from `findmnt`), because every WSL path reports the same Windows volume.
  - It needs **administrator** rights and a real distro (`docker-desktop` is skipped).
  - **Not yet tested on a real ext4 disk:** this PC has none, and the session wasn't elevated.
- **Next:** read ext2/3/4 **directly** with **DiscUtils** (MIT), without WSL. Raw reads need admin too, but there's no VM and it works on any Windows.
- **Deleted files on ext4:** ext4 zeroes block pointers on delete, so name-based undelete rarely works. **PhotoRec** carving (any file system) is the reliable route and is already in the PowerShell toolkit (Phase 5).

### Phase 7: storage connections (USB 3.x, SATA, NVMe, M.2, SD, …)
- **Done:** every drive shows how Windows sees it: USB, SATA, NVMe, SCSI/UASP, SD, MMC, SAS, RAID, iSCSI, virtual/VHD, Storage Spaces, FireWire, ATA/IDE and more.
  - It uses IOCTLs that need **no admin**, and also reports HDD/SSD, TRIM and removable.
  - Verified here: Samsung 850 EVO = SATA SSD, Kingston/Samsung = NVMe SSD, ASMT 2115 = USB (type hidden by the bridge → 1 reader), SanDisk Extreme = USB SSD.
- **What the bus means for recovery** (in `dictionaries.xml`, shown in Expert mode):
  - Recovery reads every disk the same way (`\\.\PhysicalDriveN`), so USB 3.0, SATA, NVMe and M.2 all work. **M.2 is a slot**, so an M.2 drive reports SATA or NVMe.
  - **SSD/NVMe with TRIM:** deleted files are usually wiped by the drive itself, so carving finds zeros. The UI warns about this. HDDs and SD cards are the good cases.
  - **USB bridges** often hide SMART and the HDD/SSD type. Health checks then need `smartctl -d sat` (Phase 5, external tool).
  - **iSCSI and Fibre Channel are block-level**, so unlike SMB/FTP they *can* be recovered from like a local disk.
- **Next (Phase 5 port):**
  - SMART health per bus (smartctl, external, GPL)
  - sector imaging with bad-sector skip for failing disks, so recovery works from the image
  - MTP phones/cameras (exists in PowerShell)

### Phase 8: enterprise database (several computers)
- **Done:** a SQL Server provider. Point every PC's `.env` at one server, and all catalogs land in one database. No merge is needed.
- **Alternative without a server:** each PC keeps its own SQLite catalog, and `merge` combines them (works across providers).
- **Next if needed:**
  - a PostgreSQL provider
  - `machine_name` per scan (who scanned what)
  - a read-only "report viewer" role; SQL Server permissions are enough, the app still needs no login

### Phase 9 (last, optional): network export over SFTP / FTP
**Not needed for the current setup.** This phase is designed now so the earlier phases leave the right hooks. Recovery itself never runs over a network (file protocols don't expose deleted data). The network is only a **destination**, and later perhaps a **source of normal files** to catalog.

**Two processes, one-way hand-off through folders:**
```
 Recovery/Consolidate process            Uploader process (separate exe or service)
 ----------------------------            ------------------------------------------
 writes  <BACKLOG>\x.rtpart              watches <BACKLOG> for finished files only
 renames <BACKLOG>\x   (atomic, done) -> upload to <REMOTE>/x.part over SFTP/FTP(S)
                                         verify remote size (+ hash where the server allows)
                                         rename remote x.part -> x       (atomic on the server)
                                         move local file  -> <OVERRIDE>\uploaded\x
 duplicates      -> <OVERRIDE>\duplicates\     (from Phase 2)
 unrecoverable   -> <OVERRIDE>\unrecoverable\  (from Phase 4)
```
- **The backlog is filled by one process only** (the recovery side). The uploader only *reads* from it and moves files out. No file is ever shared mid-write, because `.rtpart` files are ignored until they're renamed.
- **Override folder:** it collects everything that's safe elsewhere or not worth keeping locally: uploaded files, duplicates and unrecoverable files. When local space runs out, this folder is overwritten/cleared first (the same rule as `_Duplicates` in Phase 2). _Please confirm this reading of "override folder"; see open points._
- **Crash-safe:** a journal (in the catalog DB) records each file as `queued → uploading → uploaded → verified`. After a crash, `uploading` files are re-sent (the remote `.part` is overwritten) and nothing is lost or duplicated.
- **Protocols:**
  - **SFTP** (recommended): [SSH.NET](https://github.com/sshnet/SSH.NET), MIT. Key authentication plus host-key pinning from `.env`.
  - **FTP / FTPS:** [FluentFTP](https://github.com/robinrodricks/FluentFTP), MIT. Explicit TLS required unless `.env` says otherwise. Plain FTP sends passwords in clear text, so it's for a trusted LAN only.
- **The "middleman":** this design needs no middleman server. The uploader process *is* the middleman between the local backlog and the network share. If a relay server is wanted later (e.g. a NAS that pulls from the PC), it can use the same folder contract.

---

## 6. Hardware setup for the new workflow

**Disks:**
- **Destinations** (in the Icy Box dock, powered): 2 TB 2.5" HDD (main) + 1 TB HDD (overflow). G: (SanDisk 1 TB) is an extra overflow, or holds the existing recovery that will be merged.
- **Sources:** one at a time, directly into a laptop USB 3.0 port, especially bus-powered 2.5" drives.
- **Disks in the other PC:** catalog them on that PC and merge the database file.

**Before long runs:**
- Pause Windows Update.
- Set sleep to "Never".
- Don't unplug disks during a sort or copy.

---

## 7. Third-party code and tools (licenses checked 2026-11-12)
This repository is **MIT**. MIT, Apache-2.0 and BSD code can be included; keep each project's license notice in `THIRD-PARTY-NOTICES.md`. GPL code may **not** be copied in. GPL tools are run as **separate programs** (as PhotoRec already is), which is allowed.

| Project | License | Use here | How |
|---|---|---|---|
| [LTRData/DiscUtils](https://github.com/LTRData/DiscUtils) | MIT | Read ext2/3/4, NTFS, FAT, exFAT, ISO, VHD/VMDK from raw disks or images without mounting (Phase 6, imaging) | NuGet library |
| [sshnet/SSH.NET](https://github.com/sshnet/SSH.NET) | MIT | SFTP upload with key authentication (Phase 9) | NuGet library |
| [robinrodricks/FluentFTP](https://github.com/robinrodricks/FluentFTP) | MIT | FTP/FTPS upload (Phase 9) | NuGet library |
| [drewnoakes/metadata-extractor-dotnet](https://github.com/drewnoakes/metadata-extractor-dotnet) | Apache-2.0 | Full EXIF/XMP/QuickTime metadata, incl. video duration and HEIC; can replace the small header reader (Phase 3b) | NuGet library |
| [coenm/ImageHash](https://github.com/coenm/ImageHash) | MIT | Perceptual hashes for look-alike photos (port of `RT.Visual`; Phase 3b "has a full version") | NuGet library |
| [mono/SkiaSharp](https://github.com/mono/SkiaSharp) | MIT | Decode and verify images for the corrupt-file check; thumbnails in the UI (Phase 4) | NuGet library |
| [adamhathcock/sharpcompress](https://github.com/adamhathcock/sharpcompress) | MIT | Test/open archives (zip, 7z, rar) for corruption (Phase 4) | NuGet library |
| [DapperLib/Dapper](https://github.com/DapperLib/Dapper) | Apache-2.0 | Optional: less mapping code in repositories | NuGet library |
| [dotnet/SqlClient](https://github.com/dotnet/SqlClient) | MIT | SQL Server provider | **in use** |
| [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) | MPL-2.0 | SMART/temperature as a library. MPL is file-level copyleft: use it unmodified as a package, don't copy its files into ours | NuGet library |
| [xinntao/Real-ESRGAN-ncnn-vulkan](https://github.com/xinntao/Real-ESRGAN-ncnn-vulkan) | MIT | Upscaling orphan previews (Phase 3b) | external exe |
| [cgsecurity/testdisk](https://github.com/cgsecurity/testdisk) (PhotoRec) | GPL-2.0 | Carving deleted files (already used) | external exe only |
| [anthwlock/untrunc](https://github.com/anthwlock/untrunc) | GPL-2.0 | Repair MP4/MOV without an index (Phase 4) | external exe only |
| [smartmontools](https://github.com/smartmontools/smartmontools) | GPL-2.0 | SMART through USB bridges (`-d sat`) (Phase 5/7) | external exe only |
| [FFmpeg](https://github.com/FFmpeg/FFmpeg) | LGPL/GPL (build-dependent) | Probe/test-decode/remux videos (Phase 4) | external exe only |
| [sleuthkit/sleuthkit](https://github.com/sleuthkit/sleuthkit) | mixed (CPL/IBM/GPL parts) | Reference for file-system forensics; `fls`/`icat` as external tools if ever needed | external exe only |
| [qarmin/czkawka](https://github.com/qarmin/czkawka) | MIT outside its app crates (check per crate) | Ideas for similar-image/video detection and broken-file checks (Rust; ideas only, not code) | reference |

Reference only (idea, no code): [windirstat/windirstat](https://github.com/windirstat/windirstat) is GPL-3.0, so the Phase 2b disk map is our own implementation of the published squarified-treemap algorithm.

Reference only (idea, no code) for Phase 5b: [GNU ddrescue](https://www.gnu.org/software/ddrescue/) (GPL) for the "skip ahead, never hammer a bad area" read strategy and its mapfile idea. It can also be run as an external tool for very damaged disks.

Not used: [SixLabors/ImageSharp](https://github.com/SixLabors/ImageSharp). Its "Six Labors Split License" isn't plain open source for every use; SkiaSharp covers the need under MIT.

---

## 8. Claude Code plugins for this project
From the official `claude-plugins-official` marketplace (install with `/plugin`):

| Plugin | Why | Priority |
|---|---|---|
| **csharp-lsp** | Real C# code intelligence for Claude (errors, go-to-definition, references) across the 5 projects. Needs `dotnet tool install -g csharp-ls`. | **Install** |
| **security-guidance** | Warns when an edit touches secrets, `.env`, credentials, SQL strings or file deletion; this project has all of them. | **Install** |
| **feature-dev** | Explore → architect → implement → review workflow, a good fit for Phases 2–9. | **Install** |
| commit-commands | `/commit`, push and PR in one step. | Nice to have |
| pr-review-toolkit | Review agents for error handling, tests and type design before merging a phase. | Nice to have |
| claude-md-management | Keeps a project `CLAUDE.md` (architecture rules, "never write to source disks") up to date. | Nice to have |

Already available in this setup: `code-review`, `code-simplifier`, `engineering`, `diagrams`, and the Microsoft Learn docs connector (.NET/WinForms/SQL Server documentation). Not useful here: web-frontend plugins (`frontend-design`, `playwright`), since this is WinForms.

---

## 9. Open points
- ~~Confirm the `_Duplicates` eviction rule~~: confirmed.
- ~~Build Phase 1~~: done. ~~Restructure into 3 layers~~: done (Phase 1b).
- Choose the preview size threshold (default: longest side ≤ 640 px; now editable in `dictionaries.xml`), and whether orphan previews are upscaled automatically or only on request.
- Confirm which disk is the "controller" (reference) disk for the catalog.
- **Override folder (Phase 9):** confirm that it means "files that are safe elsewhere or not worth keeping, which may be cleared first when space runs out" (uploaded, duplicates, unrecoverable).
- Bad-sector map (Phase 5b): block size default (≈1,024 blocks per disk, or a fixed size such as 256 MB?), and the colour thresholds for "slow" and "very slow" (proposal: > 50 ms and > 300 ms per 1 MB read on an HDD).
- Test the WSL ext4 mount on a real Linux disk, as administrator (also: kill the run while mounted, then check the automatic unmount at the next start).
- ~~Risk review items: catalog on a source disk, keep the PC awake, unplugging, two-step deletion~~: built (3.6–3.8).
- Real-hardware checks (3.7 open item 6): pull a USB source and a destination during `copy` and plug them into another port; put `catalog.db` on a USB disk and try to scan that disk (must be refused).
- ~~Build Phase 2 (copy plan + dry-run report)~~: done in the CLI and the desktop app ("Copy" page).
- ~~Next to build: Phase 2b (disk map)~~: built. ~~Phase 3 (junk detection)~~: built, with keep-together folders. Next: Phase 3b (low-quality previews: look-alike matching against full photos, optional upscaling), or the disk map overlay for junk.
- **UI layout fixes (2026-11-12, user feedback: "table headers aren't aligned with their content; buttons appear off screen or near the edge").** Checked every page at the size the app really opens with (about 1200×800 on a 1920×1080 screen at 100 %), not only at 1280×1000:
  - Number columns had right-aligned values under left-aligned headers. `GridBinder` now gives each header its column's alignment, in every table.
  - Disks and Catalog pages showed an empty scrollbar. A docked, auto-scrolling TableLayoutPanel miscounts its height, and on Disks the hidden expert-only table kept its fixed 180 px row. Both pages now use the Copy page's pattern (a scrolling panel around an auto-sizing table), and the hidden row collapses to 0.
  - Copy page: the content now stretches with the window. Step 1 is the drive box with Add and Add a folder on one row, then the list with Move up / Move down / Remove / Refresh beside it (before, a tall column of buttons ran off the bottom). "What does Copy do?" has a Hide/Show explanation link that is remembered (`UiPreferences`), so steps 1–3 fit on one screen.
  - Junk and apps: the three lists are now tabs, so each gets the full height (at 800 px the lower two had shrunk to about one row).
  - A blank table on Disks right after opening is just the drive details still loading (bus, model), not a bug.
- Keep-together folders are copied without deduplication. The two Minecraft backups on F: share many identical files, so both copies take space (about 52 GB together). Confirm this is wanted, or whether only the newest backup should be kept whole and the older one go to `_Review`.
- **Duplicate check (built 2026-11-12, first real data).** Asked: "check whether the duplicates really are duplicates; there is no way to check this."
  - Built `DuplicateCheckService`: it re-reads every copy of N random duplicate groups with SHA-256 (independent of the catalog's xxHash128) and reports identical / different / not checked. Report page: "Check duplicates"; CLI: `rtcatalog verify [--top N]`.
  - **Real catalog** (F: "Folders" + G:, 141,525 files, 10,625 duplicate groups): **16 of 16 sampled groups were identical.** That covers big videos, photos, music in both `F:\Documenten\folders\muziek` and `G:\Audio`, and tiny files.
  - Findings for later phases:
    - `G:\From_E\MediaRecovery\_Duplicates` is the old PowerShell toolkit's duplicates folder, so its files show up as extra copies.
    - There are 339 identical 24-byte "mp4" files: recovery scraps, not videos (Phase 3/4).
    - `F:\postgres-data` holds database files that are identical across databases. Keep them as a set: never treat them as junk duplicates (Phase 3).
  - Window fix: at display scaling the window was taller than the screen, which hid the status bar's Stop button. It now opens maximised when it doesn't fit, otherwise centred.
  - Open question to the user: "no way of clearing that out" (see the conversation).
- **Copy page usability (2026-11-12, user feedback: "buttons are underneath the live log; I don't understand what it should do").**
  - Fixed the layout: the page didn't scroll, because a TableLayoutPanel with a stretching row can't scroll. The content now sits in a normal scrolling panel, so steps 2 and 3 are always reachable.
  - Added "What does Copy do?" at the top of the page:
    - the source disks are only read;
    - one copy of each unique file goes to the backup, sorted by type with the original folders kept;
    - `_Duplicates` and `_OlderVersions` hold the extras; nothing is overwritten; stop and continue any time;
    - an example path;
    - Copy vs Deep recovery: Copy handles visible files only; deleted files are Deep recovery (coming; for now the old toolkit).
  - The steps are named "Step 1 — Choose your backup disk", "Step 2 — Preview (nothing is written yet)", "Step 3 — Start copying". Each has an explanation, and only the next sensible button is enabled (no preview without a destination, no copy without a preview).
  - Idea for later: the same kind of "what does this do" block on the Catalog and Report pages, and a short page comparing Copy and Deep recovery.
- Next step for the data: catalog every disk (UI "Catalog disks" or `rtcatalog scan`), including the other PC, then `rtcatalog plan <2 TB disk> <1 TB disk>` and check the plan before `copy`.
