"""
media_cleanup - reference implementation of the "clean master folder" workflow (to be ported to C# later).

Goal: one master folder per type (G:\\Videos, G:\\Photos, ...) with every file once, and next to it
    <drive>\\duplicates\\<Type>\\   copies that are not needed (same content as a file in the master)
    <drive>\\review\\<Type>\\       files a person should look at (scraps, broken videos, photos that don't open)
Nothing is ever deleted or overwritten. Every move is a rename on the same drive, checked right before it happens,
written to a CSV, and mirrored in the toolkit catalog (catalog.db) so the app stays right without a rescan.

Building blocks (C# notes in brackets):
  mp4_boxes / mp4_ending_intact / xmp_size  - read the top-level MP4/MOV boxes          [BinaryReader, big-endian]
  sha256_with_head                          - SHA-256 of the whole file and of all but  [IncrementalHash.CreateHash(SHA256),
                                              the last TAIL bytes, in one read             clone state = GetCurrentHash()]
  find_near_copies                          - same size + same head hash = same video;   [ContentHasher / DuplicatePlanner]
                                              keeper = intact ending, then most tags,
                                              then lowest number in the name
  photo_opens                               - can the picture be decoded at all          [WIC / ImageSharp; HEIC needs a codec]
  is_scrap                                  - too small to be a real photo/video         [FileTagger "Scraps" rule]
  Catalog                                   - keeps catalog.db in step with each move    [IFileActionRepository.RecordMove]
  safe_move                                 - re-check, never overwrite, " (2)" on clash [FileActionService + ISourceFileOperations]

Command line: python media_cleanup.py --help
"""
from __future__ import annotations

import argparse
import collections
import csv
import datetime
import hashlib
import json
import os
import re
import sqlite3
import struct
import sys
from dataclasses import dataclass, field

TAIL = 64 * 1024                 # a "damaged/different ending" may only differ inside the last 64 KB
CHUNK = 8 * 1024 * 1024
SCRAP_VIDEO = 100 * 1024         # smaller than this is not a real video
SCRAP_PHOTO = 10 * 1024          # smaller than this is not a real photo
SEP = os.sep


# ---------------------------------------------------------------- MP4 / MOV structure

def mp4_boxes(path: str) -> list[tuple[bytes, int, int]] | None:
    """Top-level boxes as (type, offset, size). None when the file does not start like an MP4/MOV."""
    size = os.path.getsize(path)
    boxes, pos = [], 0
    with open(path, 'rb') as f:
        while pos + 8 <= size:
            f.seek(pos)
            n, kind = struct.unpack('>I4s', f.read(8))
            if n == 1:
                n = struct.unpack('>Q', f.read(8))[0]
            elif n == 0:
                n = size - pos
            if n < 8 or not kind.decode('latin-1').isprintable():
                break
            boxes.append((kind, pos, n))
            pos += n
    if not boxes or boxes[0][0] not in (b'ftyp', b'wide', b'mdat', b'moov', b'free', b'skip'):
        return None
    return boxes


def video_status(path: str) -> str | None:
    """What a person cares about, from the structure alone:
        'complete'     - header, picture+sound (mdat) and index (moov) are there and fill the file exactly: it plays
        'tags damaged' - complete, but the tag block at the end (uuid/XMP) is damaged: it still plays, only metadata is lost
        'broken'       - picture data or index missing, or the file is cut off / has data after its last box
        None           - not an MP4/MOV, cannot tell
    LESSON (2026-11-12): a damaged tag block is NOT a broken video. Treating it as broken sent 11 playable videos to review."""
    boxes = mp4_boxes(path)
    if boxes is None:
        # PhotoRec also writes pieces that start inside 'mdat' or 'wide' (names like f123_mdat.mov): no index, so broken
        try:
            with open(path, 'rb') as f:
                f.seek(4)
                return 'broken' if f.read(4) in (b'mdat', b'wide', b'moov', b'free') or os.path.getsize(path) < 64 else None
        except OSError:
            return None
    size = os.path.getsize(path)
    kinds = [k for k, _, _ in boxes]
    kind, start, n = boxes[-1]
    if b'mdat' not in kinds or b'moov' not in kinds or start + n != size:
        return 'broken'
    if kind == b'uuid':
        with open(path, 'rb') as f:
            f.seek(max(start, size - 256))
            if b'<?xpacket end=' not in f.read():
                return 'tags damaged'
    return 'complete'


STATUS_RANK = {'complete': 0, 'tags damaged': 1, None: 2, 'broken': 3}   # which copy of the same video stays


def mp4_ending_intact(path: str) -> bool | None:
    """True: the boxes cover the file exactly, a 'moov' (index) exists, and a trailing XMP block is closed.
    False: cut off, garbage at the end, or no index. None: not an MP4/MOV (cannot tell)."""
    boxes = mp4_boxes(path)
    if boxes is None:
        return None
    size = os.path.getsize(path)
    kind, start, n = boxes[-1]
    if start + n != size or not any(b[0] == b'moov' for b in boxes):
        return False
    if kind == b'uuid':
        with open(path, 'rb') as f:
            f.seek(max(start, size - 256))
            return b'<?xpacket end=' in f.read()
    return True


def xmp_size(path: str) -> int:
    """Size of the trailing metadata (uuid/XMP) block; larger = more tags."""
    boxes = mp4_boxes(path) or []
    return sum(n for kind, _, n in boxes if kind == b'uuid')


# ---------------------------------------------------------------- hashing / grouping

def sha256_with_head(path: str, tail: int = TAIL) -> tuple[str, str]:
    """(SHA-256 of the whole file, SHA-256 of everything except the last `tail` bytes) in one read."""
    size = os.path.getsize(path)
    cut = max(0, size - tail)
    h = hashlib.sha256()
    read = 0
    with open(path, 'rb') as f:
        while read < cut:
            b = f.read(min(CHUNK, cut - read))
            if not b:
                break
            h.update(b)
            read += len(b)
        head = h.copy().hexdigest()
        while b := f.read(CHUNK):
            h.update(b)
    return h.hexdigest(), head


def name_number(name: str) -> tuple[int, str]:
    m = re.search(r'\d+', name)
    return (int(m.group()) if m else 1 << 62, name.lower())


@dataclass
class Copy:
    path: str
    size: int
    mtime_ns: int
    full: str
    head: str
    intact: bool | None
    tags: int
    status: str | None = None          # video_status(): decides which copy stays


@dataclass
class Group:
    keep: Copy
    move: list[Copy] = field(default_factory=list)

    def kind(self, c: Copy) -> str:
        if c.full == self.keep.full:
            return 'exact'
        return 'broken ending' if c.intact is False else 'other metadata'


def find_near_copies(folder: str, progress=None, exact_only: bool = False) -> list[Group]:
    """Groups of files in `folder` (top level) that are the same apart from at most the last TAIL bytes.
    Files that are too small to have a meaningful 'head' (<= 1 MB) are never grouped this way.
    exact_only: only byte-for-byte copies (any size > 0); use this for photos and other small files."""
    entries = [e for e in os.scandir(folder) if e.is_file()]
    by_size = collections.defaultdict(list)
    for e in entries:
        st = e.stat()
        if st.st_size > (0 if exact_only else 1024 * 1024):
            by_size[st.st_size].append(e)
    todo = [e for g in by_size.values() if len(g) > 1 for e in g]
    total, done = sum(e.stat().st_size for e in todo), 0
    copies: dict[int, list[Copy]] = collections.defaultdict(list)
    for e in todo:
        full, head = sha256_with_head(e.path)
        st = os.stat(e.path)
        copies[st.st_size].append(Copy(e.path, st.st_size, st.st_mtime_ns, full, head, mp4_ending_intact(e.path), xmp_size(e.path), video_status(e.path)))
        done += st.st_size
        if progress:
            progress(done, total, e.name)
    groups = []
    for same_size in copies.values():
        by_head = collections.defaultdict(list)
        for c in same_size:
            by_head[c.full if exact_only else c.head].append(c)
        for cluster in by_head.values():
            if len(cluster) < 2:
                continue
            # keeper: intact ending first, then most tags, then the lowest number in the name
            cluster.sort(key=lambda c: (STATUS_RANK.get(c.status, 2), -c.tags,
                                        name_number(os.path.basename(c.path))[0], len(os.path.basename(c.path)),
                                        name_number(os.path.basename(c.path))[1]))
            groups.append(Group(cluster[0], cluster[1:]))
    return groups


# ---------------------------------------------------------------- same video, whatever the tags at the end

MEDIA_BOXES = (b'ftyp', b'mdat', b'moov')      # header, picture+sound, index: what makes the video


def media_shape(path: str) -> tuple | None:
    """Sizes of the boxes that make the video (cheap: only box headers are read). None when not an MP4/MOV
    or when one of them is missing. Two files can only be the same video when their shapes are equal."""
    try:
        boxes = mp4_boxes(path)
    except OSError:
        return None
    if not boxes:
        return None
    sizes = {k: n for k, _, n in boxes if k in MEDIA_BOXES}
    if b'mdat' not in sizes or b'moov' not in sizes:
        return None
    return tuple(sizes.get(k, 0) for k in MEDIA_BOXES)


def media_hash(path: str) -> str:
    """SHA-256 over the header, picture+sound and index boxes only; trailing tag blocks (uuid/XMP, free) are ignored.
    Same media_hash = same video, byte for byte, even when one copy has more, fewer or broken tags at the end.  [C#: same
    walk as mp4_boxes, IncrementalHash over each box range]"""
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for kind, start, n in mp4_boxes(path) or []:
            if kind not in MEDIA_BOXES:
                continue
            h.update(kind)
            f.seek(start)
            left = n
            while left > 0:
                b = f.read(min(CHUNK, left))
                if not b:
                    raise OSError(f'{path}: cut off inside {kind.decode()}')
                h.update(b)
                left -= len(b)
    return h.hexdigest()


def find_same_media(folder: str, progress=None) -> list[Group]:
    """Videos in `folder` (top level) with the same media_hash. Keeper: intact ending, then most tags, then lowest number."""
    shapes = collections.defaultdict(list)
    for e in os.scandir(folder):
        if e.is_file() and (s := media_shape(e.path)):
            shapes[s].append(e.path)
    todo = [p for ps in shapes.values() if len(ps) > 1 for p in ps]
    total, done = sum(os.path.getsize(p) for p in todo), 0
    by_hash = collections.defaultdict(list)
    for p in todo:
        try:
            key = media_hash(p)
        except OSError:
            continue                        # unreadable/cut off: the review check reports it
        st = os.stat(p)
        full = ''                           # not needed for the decision; kind() then never says 'exact'
        by_hash[key].append(Copy(p, st.st_size, st.st_mtime_ns, full, key, mp4_ending_intact(p), xmp_size(p), video_status(p)))
        done += st.st_size
        if progress:
            progress(done, total, os.path.basename(p))
    groups = []
    for cluster in by_hash.values():
        if len(cluster) < 2:
            continue
        cluster.sort(key=lambda c: (STATUS_RANK.get(c.status, 2), -c.tags,
                                    name_number(os.path.basename(c.path))[0], len(os.path.basename(c.path)),
                                        name_number(os.path.basename(c.path))[1]))
        groups.append(Group(cluster[0], cluster[1:]))
    return groups


def same_media_kind(keep: Copy, other: Copy) -> str:
    if other.size == keep.size and other.tags == keep.tags and other.intact == keep.intact:
        return 'exact'
    if other.status == 'broken':
        return 'broken'
    if other.status == 'tags damaged':
        return 'damaged tags'
    return 'fewer or no tags' if other.tags < keep.tags else 'other tags'


# ---------------------------------------------------------------- review checks

def is_scrap(path: str, kind: str) -> bool:
    """Too small to be real AND not a complete file.
    LESSON (2026-11-12): size alone is wrong. Two complete clips of 53 KB and 85 KB (very short videos) were sent to review."""
    size = os.path.getsize(path)
    if size >= (SCRAP_VIDEO if kind == 'Videos' else SCRAP_PHOTO):
        return False
    if kind == 'Videos':
        return video_status(path) != 'complete' and video_status(path) != 'tags damaged'
    return True


_heif_ready = False


def photo_opens(path: str) -> tuple[bool, str]:
    """(True, '') when the picture decodes completely; (False, why) otherwise. Needs Pillow (+ pillow-heif for HEIC)."""
    global _heif_ready
    from PIL import Image
    if not _heif_ready:
        try:
            import pillow_heif
            pillow_heif.register_heif_opener()
        except ImportError:
            pass
        _heif_ready = True
    try:
        with Image.open(path) as im:
            im.load()               # decodes every pixel: a cut-off or damaged file fails here
        return True, ''
    except Exception as ex:     # noqa: BLE001 - any decoder error means "does not open"
        return False, f'{type(ex).__name__}: {ex}'[:200]


# ---------------------------------------------------------------- catalog + safe moves

class Catalog:
    """The toolkit catalog (SQLite). A moved file keeps its row (and fingerprints); only its path changes."""

    def __init__(self, db_path: str, drive_root: str):
        self.db = sqlite3.connect(db_path)
        root = drive_root.rstrip(SEP) + SEP
        row = self.db.execute('select id from disks where last_root = ?', (root,)).fetchone()
        if row is None:
            raise SystemExit(f'{root} is not in the catalog')
        self.disk = row[0]
        self.root = root

    def rel(self, path: str) -> str:
        return path[len(self.root):]

    def row(self, path: str):
        return self.db.execute('select id, size, full_hash from files where disk_id = ? and path = ? and missing = 0',
                               (self.disk, self.rel(path))).fetchone()

    def present_with_hash(self, full_hash: bytes, size: int) -> list[str]:
        return [self.root + r[0] for r in self.db.execute(
            'select path from files where disk_id = ? and full_hash = ? and size = ? and missing = 0', (self.disk, full_hash, size))]

    def moved(self, old: str, new: str) -> None:
        stamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%S.%f')[:-3] + 'Z'
        r = self.row(old)
        if r is None:
            return
        self.db.execute('delete from files where disk_id = ? and path = ? and id <> ? and missing = 1', (self.disk, self.rel(new), r[0]))
        self.db.execute('update files set path = ?, missing = 0, last_seen = ? where id = ?', (self.rel(new), stamp, r[0]))

    def commit(self) -> None:
        self.db.commit()


def free_name(target: str, taken: set[str]) -> str:
    """target, or 'name (2).ext', 'name (3).ext', ... when something is (or will be) there."""
    if not os.path.exists(target) and target.lower() not in taken:
        return target
    base, ext = os.path.splitext(target)
    n = 2
    while os.path.exists(f'{base} ({n}){ext}') or f'{base} ({n}){ext}'.lower() in taken:
        n += 1
    return f'{base} ({n}){ext}'


@dataclass
class Move:
    src: str
    dst: str
    why: str
    size: int | None = None          # expected size (None: take it from the disk now)
    mtime_ns: int | None = None      # expected modified time (None: not checked)
    keep: str | None = None          # a copy that must still be there (for duplicates)
    keep_size: int | None = None


def run_moves(moves: list[Move], catalog: Catalog | None, report_csv: str, apply: bool) -> collections.Counter:
    """Dry run (apply=False) or real run. Every move is re-checked right before it happens; nothing is overwritten."""
    counts = collections.Counter()
    taken: set[str] = set()
    out = []
    for m in moves:
        status, dst = 'ok', m.dst
        if not os.path.exists(m.src):
            status = 'skip: source is gone'
        elif m.size is not None and os.path.getsize(m.src) != m.size:
            status = 'skip: size changed'
        elif m.mtime_ns is not None and abs(os.stat(m.src).st_mtime_ns - m.mtime_ns) > 2_000_000_000:
            status = 'skip: modified time changed'
        elif m.keep is not None and (not os.path.exists(m.keep) or (m.keep_size is not None and os.path.getsize(m.keep) != m.keep_size)):
            status = 'skip: the copy that should stay is missing or changed'
        elif os.path.splitdrive(m.src)[0].lower() != os.path.splitdrive(m.dst)[0].lower():
            status = 'skip: not on the same drive'
        else:
            dst = free_name(m.dst, taken)
            taken.add(dst.lower())
            if apply:
                os.makedirs(os.path.dirname(dst), exist_ok=True)
                os.rename(m.src, dst)                      # same drive: instant; fails instead of overwriting
                if catalog:
                    catalog.moved(m.src, dst)
                status = 'moved'
        counts[status] += 1
        out.append((status, m.why, m.src, dst))
    if apply and catalog:
        catalog.commit()
    with open(report_csv, 'w', newline='', encoding='utf-8-sig') as f:
        w = csv.writer(f, delimiter=';')
        w.writerow(['status', 'why', 'from', 'to'])
        w.writerows(out)
    return counts


def remove_empty_folders(folder: str) -> int:
    """Removes folders under (and including) `folder` that are completely empty. Returns how many."""
    removed = 0
    for d, _, _ in sorted(os.walk(folder), key=lambda x: -len(x[0])):
        if os.path.isdir(d) and not os.listdir(d):
            os.rmdir(d)
            removed += 1
    return removed


# ---------------------------------------------------------------- command line

def _progress(done: int, total: int, name: str) -> None:
    print(f'  {done / 2**30:7.1f} / {total / 2**30:.1f} GB  {name}', flush=True)


def main(argv=None) -> None:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest='cmd', required=True)
    c = sub.add_parser('near-copies', help='read-only: find same/near-same files in one folder, write a JSON + CSV list')
    c.add_argument('folder'); c.add_argument('out_json'); c.add_argument('out_csv')
    c.add_argument('--exact-only', action='store_true', help='only byte-for-byte copies (photos, small files)')
    v = sub.add_parser('same-video', help='read-only: videos with the same picture+sound+index, whatever their tags; JSON + CSV list')
    v.add_argument('folder'); v.add_argument('out_json'); v.add_argument('out_csv')
    r = sub.add_parser('review-check', help='read-only: scraps, broken videos without a good twin, photos that do not open')
    r.add_argument('folder'); r.add_argument('kind', choices=['Videos', 'Photos']); r.add_argument('out_csv')
    a = p.parse_args(argv)

    if a.cmd == 'near-copies':
        groups = find_near_copies(a.folder, _progress, exact_only=a.exact_only)
        data = [{'keep': g.keep.__dict__, 'move': [m.__dict__ | {'kind': g.kind(m)} for m in g.move]} for g in groups]
        json.dump(data, open(a.out_json, 'w', encoding='utf-8'), indent=1)
        with open(a.out_csv, 'w', newline='', encoding='utf-8-sig') as f:
            w = csv.writer(f, delimiter=';')
            w.writerow(['moves out', 'stays', 'kind', 'MB'])
            for g in groups:
                for m in g.move:
                    w.writerow([os.path.basename(m.path), os.path.basename(g.keep.path), g.kind(m), f'{m.size / 2**20:.1f}'])
        print(collections.Counter(g.kind(m) for g in groups for m in g.move))
    elif a.cmd == 'same-video':
        groups = find_same_media(a.folder, _progress)
        data = [{'keep': g.keep.__dict__, 'move': [m.__dict__ | {'kind': same_media_kind(g.keep, m)} for m in g.move]} for g in groups]
        json.dump(data, open(a.out_json, 'w', encoding='utf-8'), indent=1)
        with open(a.out_csv, 'w', newline='', encoding='utf-8-sig') as f:
            w = csv.writer(f, delimiter=';')
            w.writerow(['moves to duplicates', 'stays in the master', 'why', 'MB', 'tags (moves / stays)'])
            for g in groups:
                for m in g.move:
                    w.writerow([os.path.basename(m.path), os.path.basename(g.keep.path), same_media_kind(g.keep, m),
                                f'{m.size / 2**20:.1f}', f'{m.tags} / {g.keep.tags}'])
        kinds = collections.Counter(same_media_kind(g.keep, m) for g in groups for m in g.move)
        print('RESULT', len(groups), 'groups;', dict(kinds), f'{sum(m.size for g in groups for m in g.move) / 2**30:.1f} GB')
    elif a.cmd == 'review-check':
        rows = []
        for e in os.scandir(a.folder):
            if not e.is_file():
                continue
            if is_scrap(e.path, a.kind):
                rows.append((e.name, e.stat().st_size, 'scrap: too small'))
            elif a.kind == 'Videos':
                if video_status(e.path) == 'broken':     # only missing picture data/index or cut off; damaged tags are fine
                    rows.append((e.name, e.stat().st_size, 'video broken: picture data or index missing, or cut off'))
            else:
                ok, why = photo_opens(e.path)
                if not ok:
                    rows.append((e.name, e.stat().st_size, 'does not open: ' + why))
        with open(a.out_csv, 'w', newline='', encoding='utf-8-sig') as f:
            w = csv.writer(f, delimiter=';')
            w.writerow(['file', 'bytes', 'why'])
            w.writerows(rows)
        print(collections.Counter(r[2].split(':')[0] for r in rows))


if __name__ == '__main__':
    main()
