# Feature review and build instructions (2026-11-21)

Source: review of `PLAN.md`, the code in `src/`, and `docs/cleanup-session-2026-11-12.md`.
Layering rule for everything below: **UI/CLI → BLL ← DAL**. Pure logic and interfaces go in `RecoveryBLL`, SQL and
Windows APIs in `RecoveryDAL`, WinForms in `RecoveryUI`. Every change needs xUnit tests in `tests/RecoveryBLL.Tests`.
Safety rules that apply to every feature (from memory and the cleanup session): ask before moving real data, never
delete, never overwrite (name clash → ` (2)`), never hide folders, never pick "the original" by rule alone.

## 1. Status: what is built, what is missing

| Area | State | Gap / improvement |
|---|---|---|
| Catalog, fingerprint, report, copy plan, disk map, junk, cleanup | Built | Cleanup logic exists only as Python reference (`tools/media_cleanup`) and in `CleanupService`; check parity |
| **Phase 3b** low-quality previews | Only `previewMaxLongSide` in `dictionaries.xml` | Not built: classification, look-alike match, move/delete/upscale |
| **Phase 4** corrupt files | Not built | Scan, `_Corrupt` journal, repair queue |
| **Phase 5** port PowerShell (PhotoRec, undelete, imaging, look-alikes) | Not built | PowerShell toolkit still needed |
| **Phase 5b** surface scan / bad-sector map | Not built | Raw reader, block map, SMART, files-on-bad-sector |
| Video duration (ffprobe) | "Isn't done yet" (PLAN Phase 1) | Needed for scrap rule and Phase 4 |
| Disk map overlays (junk, previews, bad sectors) | Not built | Needs Phase 3/3b/5b data |
| Per-folder "this is not junk" exceptions | Edit XML only | Needs an exceptions store + UI |
| Keep-together duplicates (2 Minecraft backups, 52 GB) | Open question in PLAN §9 | Needs user decision |
| Memory: whole catalog in RAM (200–300 MB / M files) | Known (PLAN §3.7.4) | Stream per disk |
| Perceptual hash (`RT.Visual`) | PowerShell only | Port with ImageHash |
| Real-hardware tests (USB pull, bad sector, WSL ext4) | Not done | Manual checklist |
| ext4 via DiscUtils, PostgreSQL provider, `machine_name` per scan | Planned, later | Low priority |
| Tests | 9 test files, BLL only | No UI/controller tests, no DAL-only tests for SQL Server by default |

Priority order: **A** Phase 3b → **B** video duration → **C** Phase 4 → **D** Phase 5b → **E** Phase 5 port →
**F** overlays/exceptions → **G** hardening (streaming, tests, hardware checks).

## 2. Instruction list

### A. Phase 3b: low-quality previews
1. **BLL/Models/PreviewRules.cs**: `record PreviewRules(int MaxLongSide, IReadOnlySet<(int,int)> KnownSizes, double MinBytesPerPixel)`.
   Load from the existing `junk` dictionary (`previewMaxLongSide`) in `DictionaryService`; add `previewKnownSize` entries to `config/dictionaries.xml`.
2. **BLL/Services/PreviewClassifier.cs** (pure, static, like `FileTagger`):
   `Classify(IReadOnlyList<FileFactsDto>, PreviewRules) → IReadOnlyList<PreviewCandidate>` with
   `record PreviewCandidate(long FileId, PreviewKind Kind)` and `enum PreviewKind { None, Preview, Thumbnail }`.
   Rule: Photos only, `Width/Height` known, no camera data, longest side ≤ `MaxLongSide` (or known size). Camera data always protects.
   Must not tag files that `FileTagger` already tagged (junk wins) and must not touch keep-together folders.
3. **BLL/Interfaces/IPerceptualHasher.cs**: `ulong Hash(Stream image)`; `int Distance(ulong, ulong)`.
   **DAL** implementation `ImageHashPerceptualHasher` using NuGet `CoenM.ImageHash` (MIT) + SkiaSharp.
4. **BLL/Services/PreviewMatchService.cs**: for each candidate, find a full-size photo whose perceptual hash is within a distance threshold and whose
   width/height are larger → `HasOriginal`; else `Orphan`. Store hashes in a new column `files.phash` (add to `ICatalogStore.EnsureCreated` migration for SQLite **and** SQL Server).
5. **IReportRepository.SavePerceptualHashes / ReadPerceptualHashes** (both providers).
6. **Actions** reuse the cleanup layout: `previews\HasOriginal`, `previews\Orphan` on the same disk, moves only after a listed + confirmed plan (reuse `CleanupService` plan/confirm flow and `PathSafetyValidator`).
7. **Upscale (optional)**: `IUpscaler` in BLL, DAL runs `realesrgan-ncnn-vulkan.exe` as a process with timeout; keep the original; write `<name>_upscaled.jpg` to `Photos\_Upscaled`.
8. **UI**: `PreviewsView` + `PreviewsController` + view model, following `JunkView`/`JunkController`: tiles, two tabs (HasOriginal / Orphan), threshold box, Move / Delete (two-step) / Upscale buttons; add to sidebar in `MainForm`.
9. **CLI**: `rtcatalog previews [--max-side N]`.
10. **Tests**: `PreviewClassifierTests` (camera protects, known sizes, junk wins, threshold edge), `PreviewMatchTests` with a fake hasher.

### B. Video duration
1. `IMediaProbe` in BLL (`TimeSpan? Duration`, `bool Opens`); DAL `FfprobeMediaProbe` (external process, 30 s timeout, never throws).
2. Add `duration_ms` and `opens_ok` to `FingerprintStage` as a new stage `MediaInfo`; extend `FingerprintResultDto`.
3. Feed duration into `FileFactsDto` so scrap rules can use "complete video" instead of size only (accident #10 in the cleanup record).

### C. Phase 4: corrupt files
1. **BLL/Models/Corruption.cs**: `enum FileHealth { Ok, Corrupt, Truncated, Unknown }`, `record HealthResult`.
2. **BLL/Services/FileHealthChecker.cs** with one `IFileCheck` per kind (strategy pattern): `ImageCheck` (full decode via SkiaSharp), `VideoCheck` (uses `Mp4Structure` + `IMediaProbe`), `PdfOfficeCheck` (structure), `ArchiveCheck` (sharpcompress).
3. **BLL/Services/CorruptScanService.cs**: walks a folder (reuse `IFileSystemWalker`), checks files, writes a plan; **moving to `_Corrupt\` needs user OK** (plan → confirm → move).
4. **Journal**: `ICorruptJournal` (DAL `CorruptJournalFile`, JSON lines like `CleanupLogFile`) stores original path per file; undo restores them.
5. **Repair queue**: `RepairService` with state per file (Pending / Repaired / Unrecoverable / CrashesTool after 2 attempts); each tool call runs as a separate process with timeout (`IProcessRunner`).
6. **UI/CLI**: `CorruptView` + `CorruptController`; `rtcatalog corrupt scan|repair|undo`.
7. **Tests**: truncated mp4 fixture, JPEG cut in half, crash-twice-marks-skip, undo restores paths.

### D. Phase 5b: surface scan and bad-sector map
1. **BLL**: `SurfaceBlock(int Index, double ReadMs, BlockStatus Status)`, `enum BlockStatus { Untested, Fast, Slow, VerySlow, Unreadable }`, `SurfaceScanResult`, `ISurfaceReader` (reads a block range, returns timing/error), `ISmartProvider`.
2. **BLL/Services/SurfaceScanService.cs**: loops blocks, skip-ahead on error (no retry loops), safety stop after N unreadable blocks, stop/resume, run-length compression of equal blocks (`BlockRunCompressor`, pure, unit-tested).
3. **DAL**: `PhysicalDriveReader` (`\\.\PhysicalDriveN`, needs admin, read-only handle, `TimeoutStream` for the 60 s deadline), `SmartctlProvider` (`smartctl -d sat` through USB bridges, JSON output).
4. **Storage**: tables `surface_scans` and `surface_blocks` in both providers; `files.on_bad_sector` flag.
5. **Files on bad sectors**: `ISectorFileMapper` (NTFS via DiscUtils or `FSCTL_GET_RETRIEVAL_POINTERS`) → flag catalog files; copy planner prefers the healthy copy, Phase 4 checks them first.
6. **UI**: `HealthView` (block grid custom control like `TreemapControl`, speed graph, SMART table, verdict line), Simple vs Expert split; export PNG/CSV.
7. **Tests**: run-length compression, skip-ahead with a fake reader, verdict thresholds (open point: 50 ms / 300 ms per MB).

### E. Phase 5: port the remaining PowerShell features
Port in this order, each as BLL service + DAL process wrapper + CLI command + UI page: (1) look-alike duplicates (reuses A.3),
(2) PhotoRec deep recovery (`IProcessRunner`), (3) quick scan / undelete, (4) imaging with bad-sector skip (writes the Phase 5b block map),
(5) MTP phones. Then retire the PowerShell toolkit and update README.

### F. Overlays and exceptions
1. `DiskMapService`: add `OverlayKind { Junk, Preview, BadSector }`; `TreemapControl` draws the outline.
2. Exceptions: `IExceptionRepository` (table `junk_exceptions(disk_id, path_prefix)`), applied in `FileTagger.Tag`; context menu "this is not junk" in `JunkView`.

### G. Hardening
1. Stream the plan per disk instead of loading the whole catalog (`IConsolidationRepository.GetPlanFiles` → per-disk enumerator).
2. Add controller tests with fake views (`IViews.cs` interfaces already allow it).
3. CI: GitHub Actions running `dotnet test` (SQLite only).
4. Manual hardware checklist in PLAN §9 (USB pull during copy, catalog on source disk refused, ext4 mount as admin).
5. Open decision for the user: keep the two Minecraft backups whole (52 GB) or only the newest.

## 3. Delegation to the Junior (local qwen-code via `pi`)

The Junior only gets small, pure, well-specified tasks with exact signatures and a test to make pass (A.1, A.2, A.10,
D.2 `BlockRunCompressor`). Claude reviews and runs `dotnet build` / `dotnet test` after each task. The Junior never
touches real disks, `catalog.db`, UI designer files or `.env`.
