# Cleanup session 2026-11-12: what was expected, what went wrong, the rules that came out of it

Session record for the "Clean up duplicates / Junk and apps" work on disk **G:** (SanDisk, exFAT, serial `315E7430`).
Use this as the specification when the cleanup is rebuilt in the C# app. The reference code is
[`tools/media_cleanup/media_cleanup.py`](../tools/media_cleanup/media_cleanup.py).

---

## 1. What the user expected (in their words, then as rules)

> "When I want to move duplicates, I want to move the duplicated files, not create new ones, not hide the real ones as
> system, not create a maze where the files should be. It's fine when I make a folder duplicates and you mention the
> disk, then the folder, but not some crazy stuff like that."

> "Ask me every question about anything first and then change the plan."

> "I cannot afford those mistakes with real data."

As rules:

1. **Ask first, then act.** Every move needs a list the user has seen and an explicit OK. A dry run is not an OK.
2. **The master folder holds the originals.** `G:\Videos`, `G:\Photos` (...) are the real collection. A cleanup only
   ever takes the *extra* copies out of the folder being cleaned; it never moves the original out of a master folder.
3. **Only move what was asked.** "Move the duplicates" means the duplicate files, nothing else. Never create copies.
4. **Never hide anything.** No Hidden/System attributes on folders the user works with, ever.
5. **Simple, readable layout. No maze.** Agreed layout:
   ```
   G:\
   ├── Videos\  Photos\  Audio\  Other\     masters: every file once
   ├── duplicates\Videos\  duplicates\Photos\  duplicates\Other\    the copies that are not needed
   ├── review\Videos\  review\Photos\        scraps, broken files, files that don't open: for the user to check
   └── previews\                             small preview images (to check for upscaling)
   ```
   File goes to `duplicates\<Type>\<file name>`. No `Disk_315E7430\From_E\MediaRecovery\...` chains.
6. **Never delete, never overwrite.** Moves are renames on the same disk. A name clash gets ` (2)`.
7. **Which copy stays** (videos): the complete one; then the one with the most tags; then the lowest number in the name.
8. **What counts as a duplicate** (videos): byte-for-byte copies, and the *same video* (same header + picture/sound +
   index) whatever its tag block at the end says (more tags, fewer tags, no tags, damaged tags).
   Not counted: re-encoded or trimmed versions (different picture data).
9. **Review folder, not judgement.** Scraps, broken videos without an intact twin, photos that don't open: put aside in
   `review\` for the user to look at. Never delete them.
10. **Reusable code.** Every check is written so it can be ported to C# (see the module's header for the C# mapping).

---

## 2. What went wrong (accidents), and why

| # | What happened | Root cause | Fixed by |
|---|---|---|---|
| 1 | The app's "Clean up duplicates" moved **1,547 originals** out of `G:\Photos` (1,414), `G:\Audio` (132) and `G:\Videos` (1) into `G:\process` | Keep rule "oldest date" picked the copy that stays per group without knowing which folder is the master; the copy in the recovery dump `From_E` was "older", so the original in the sorted folder was moved | Undone (all 14,078 files back). Folder scope: "Only clean up inside this folder" - the copy that stays is always outside that folder. Confirmation lists where files come from. |
| 2 | Moved files landed in `G:\process\Disk_315E7430\From_E\MediaRecovery\Videos\...` | Layout `<dest>\<disk>\<original path>` copied the whole source path: a maze | Layout is now `duplicates\<Type>\<file>` (rule 5) |
| 3 | `G:\From_E` was invisible in Explorer | An earlier session's `move-E-to-G.ps1` used `robocopy E:\ G:\From_E`; a drive root is Hidden+System and robocopy copied those attributes onto the destination folder | Attributes removed. **Any future robocopy of a drive root must use `/A-:SH`, or clear the attributes afterwards.** |
| 4 | "Missing videos": 1,323 videos (117 GB) in `From_E` were never in `G:\Videos` | The E: recovery was never merged into the sorted folders; the folder was hidden (3) | Merged into `G:\Videos` |
| 5 | Same video twice in `G:\Videos` (e.g. `f1761409680` / `f1783880064`) not found | The tool only treated byte-for-byte equal files as duplicates; these differ only in the last bytes (damaged tag block) | Same-size rule "identical except the last 64 KB" |
| 6 | First comparison would have grouped unrelated tiny files | "Identical except the last 64 KB" means *nothing* for files under 64 KB | Near-copy rule only for files > 1 MB; photos exact-only |
| 7 | 81 files labelled "damaged ending" were actually complete | Label assumed "differs at the end" = broken | Re-checked structure; split into "broken ending" / "other metadata" |
| 8 | `f140560696` / `f416357280` (same video, one with tags, one without) not found | "Same size" rule: a missing tag block changes the size | **Same-video rule**: hash only ftyp + mdat + moov (header, picture+sound, index); ignore tag blocks |
| 9 | **11 playable videos sent to review as "broken"** (user put them back) | Check called a video broken when its XMP tag block was damaged. The video itself (picture, sound, index) was complete | `video_status()`: *broken* = picture data or index missing, or cut off. Damaged tags = "tags damaged", still plays, stays in the master |
| 10 | **2 complete short clips (53 KB, 85 KB) sent to review as "scrap"** (user put them back), and 23 more like them are still in review | Scrap rule used size only (< 100 KB) | Scrap = small **and** not a complete video. 23 complete clips in `G:\review\Videos` were left there at the user's choice, to check by hand |

**Pattern behind most accidents:** a rule decided something the user had not decided (which copy is the original,
what "broken" means, what "small" means), and the result was not shown before acting. Hence rule 1.

---

## 3. Final state of G: (end of session)

| Folder | Files | Size |
|---|---:|---:|
| `G:\Videos` | 2,046 (2,033 + 13 put back) | ~236 GB |
| `G:\Photos` | 14,405 | 28.1 GB |
| `G:\Audio` | 991 | 1.4 GB |
| `G:\Other` | 15 | 0.1 GB |
| `G:\duplicates\Videos` / `Photos` / `Other` | 2,110 / 10,942 / 11 | 158.6 / 22.3 / 0.0 GB |
| `G:\review\Videos` | 52 (29 really broken, 23 complete short clips for the user to check) | 3.3 GB |
| `G:\review\Photos` | 3,885 (3,776 scraps < 10 KB, 109 don't open) | 0.1 GB |
| `G:\previews` (+ `old-overview-page`) | 2,496 (+ 8) | 0.0 GB |
| `G:\_RecoveryToolkit\From_E-logs` | 2 logs of the E: recovery | |

`G:\From_E` no longer exists (everything moved out, empty folders removed). Nothing was deleted. The catalog
(`catalog.db`) follows every move, including the 13 files the user moved back by hand.

Open for the user: the 23 complete short clips in `G:\review\Videos`, the 109 photos that don't open (a few may be
formats the checker can't read: 1 `.svg`, some `.tif`/`.bmp`), and `G:\previews`.

---

## 4. Rules in code (`tools/media_cleanup/media_cleanup.py`)

| Function | Rule |
|---|---|
| `video_status(path)` | `complete` / `tags damaged` / `broken` (mdat or moov missing, cut off, data after the last box) / `None` (not MP4/MOV) |
| `media_hash(path)` | SHA-256 over ftyp + mdat + moov only: same value = same video whatever the tags |
| `find_same_media(folder)` | groups by box sizes first (cheap), hashes only candidates; keeper = complete > tags damaged > unknown > broken, then most tags, then lowest number |
| `find_near_copies(folder, exact_only)` | byte-for-byte copies (photos, small files) |
| `is_scrap(path, kind)` | small (< 100 KB video, < 10 KB photo) **and** not a complete video |
| `photo_opens(path)` | decodes every pixel (Pillow + pillow-heif for HEIC) |
| `run_moves(...)` | dry run, then re-check size/mtime/kept copy right before each rename; never overwrites; CSV of every move; catalog updated |

---

## 5. The C# app: the "Clean up" page (designed with the user, then built the same evening)

**The user's design answers:**
- Own sidebar page **"Clean up"** (English UI). Duplicates and junk on one page. The old Report tab "Clean up duplicates"
  and the Move/Delete bar on "Junk and apps" are gone; Junk and apps is read-only again.
- **Source** = the folder or drive to clean. **Target** = where the *unwanted* files go, never the wanted versions.
- **Merge** (the From_E case): source = the dump, plus a master location; the wanted files go to `<master>\<type>`,
  and what is already in a master folder **always stays**. Only when the master copy is broken and the source copy is
  complete, the person decides (nothing in that group moves).
- Target layout **category, then type**: `duplicates\Exact copies\Videos`, `duplicates\Same video\Videos`,
  `duplicates\Broken\Photos`, `duplicates\Small files\Photos`, plus `Thumbnails and icons`, `Caches and system files`,
  `Image sets`, `App folders` (app folders keep their own structure, never split).
- Each category is an option (tick boxes; they only filter, no new Find needed).
- **Always move first.** Deleting is a separate menu, "Empty the clean-up folder", with both deletion steps.
- **Undo last clean up** puts the last run back (undo record: `%LOCALAPPDATA%\RecoveryToolkit\logs\cleanup\*.csv`).
- The page **reads the folders themselves**. The catalog is for deep recovery (where a file belongs); it never decides
  whether something is a duplicate or broken, because it can hold files that are gone. It is only kept in step.

**Built:** `CleanupService` (BLL), `Mp4Structure` / `ImageIntegrity` (structure checks), `ICatalogSync` + `CleanupLogFile`
(DAL), `CleanupView` / `CleanupController` (UI), `CleanupTests` (every accident in section 2 is a test case).
Checked read-only on the real disk: `G:\Videos` → nothing to move (the 13 put-back videos stay);
`G:\review\Videos` → 29 broken, the 23 complete clips stay. Same verdicts as the Python reference.

**Still open:**
- **Scrap rule in `config/dictionaries.xml`** (`<scrap category="Videos" below="16384"/>`, used by the Copy page) has the
  size-only flaw of accident 10. The Clean up page does not use it; the Copy page still does.
- Photos: the C# check is structural (JPEG end marker, PNG IEND, GIF trailer). HEIC/WebP/TIFF are not judged (the Python
  check decoded them with Pillow).
- **Never set or copy Hidden/System**: nothing in the toolkit does; a warning when a chosen folder is hidden is not built.

---

## 6. Where everything is

- Readable record: this file.
- Raw conversation transcript (all messages and tool output): `docs/sessions/2026-11-12-cleanup-session.jsonl`
  (contains personal paths; kept out of git by `.gitignore`).
- Per-step move lists (CSV: status, why, from, to) and the one-off step scripts: `docs/sessions/2026-11-12-move-lists/`.
  These are the "undo" record: every row says where a file came from.
- Catalog backups before each step (temporary folder, not copied):
  `C:\Users\TIMOBE~1\AppData\Local\Temp\claude\549e59e098b8ee21\05a5dcaf-ed19-4602-811d-828b08e1215f\scratchpad\undo\catalog-before-*.db`
- Tool log of the app runs: `%LOCALAPPDATA%\RecoveryToolkit\logs\toolkit-20261112.log`
