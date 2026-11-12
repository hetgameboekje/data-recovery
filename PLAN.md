# Recovery Toolkit – Plan & Project Notes

_Last updated: 2026-11-12_

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

## 3. Decision: which language for the next version

| Option | Verdict |
|---|---|
| PowerShell | At its limit: one 210 KB file, slow loops over millions of files, and the GUI and threads rely on workarounds. A persistent hash database and a repair queue would make this worse. |
| C | No. Raw disk reads and hashing take a few lines in any language. C would cost months on the GUI and file handling for no benefit. |
| Delphi | Can do the job, but it's niche, has few libraries and is hard to get help with. |
| **C# / .NET 8 (chosen)** | The existing embedded C# (`RT.Scanner`, `RT.Visual`) moves over almost as-is. It has fast parallel hashing, SQLite (`Microsoft.Data.Sqlite`), crash-safe background jobs, the same Windows APIs, and builds to a single `.exe`. |

The real recovery work is done by external tools (PhotoRec, ffmpeg, untrunc) in any language. **Keep using the PowerShell toolkit** until each feature has been ported.

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
> **Built 2026-11-12:** `src/RecoveryCatalog` → `rtcatalog.exe` (.NET 10, the current LTS; usage in README.md).
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

### Phase 2: Consolidate (copy plan)
Each file gets one of four labels:
1. **Unique** → main folders (Photos/Videos/…) on the backup disk, then the overflow disk.
2. **100% identical to a file already kept** → `_Duplicates` folder. Switch: move there / skip entirely.
3. **Same name, different content** (stale manual copies) → the **newest** goes to main and older ones go to `_OlderVersions`. Never deleted automatically.
4. **Already on the destination** → nothing to do.

**`_Duplicates` rule (on/off):** it is the lowest priority. Only when both the backup and overflow disks are full are duplicates deleted (oldest first) to make room for unique files. A unique file is never deleted to make room. _(Confirmed 2026-11-12.)_

**Dry-run report first**, e.g. "1.31 TB unique, 820 GB duplicates, 4,200 older versions → fits on 2 TB."

Resume safety stays the same: `.rtpart` files, a journal, and pausing when everything is full.

### Phase 3: Junk detection (moved to `_Review\Junk`, never deleted)
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

## 7. Open points
- ~~Confirm the `_Duplicates` eviction rule~~: confirmed.
- Choose the preview size threshold (default: longest side ≤ 640 px) and whether orphan previews are upscaled automatically or only on request.
- Confirm which disk is the "controller" (reference) disk for the catalog.
- ~~Build Phase 1 (catalog + database) in C#~~: done (`rtcatalog`).
- Next step: catalog every disk (`rtcatalog scan`), including those in the other PC (then `rtcatalog merge`), and run G: after its sort finishes. Then build Phase 2 (copy plan + dry-run report).
