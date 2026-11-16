"""Move every video in G:\\From_E\\MediaRecovery\\Videos into G:\\Videos (same disk: a rename) and keep the catalog in step.

Usage: python merge_videos.py <catalog.db> <report.csv> [--apply]
Without --apply it only checks. Never overwrites: a file whose name already exists in G:\\Videos is skipped.
"""
import csv, datetime, os, sqlite3, sys

BS = chr(92)
db_path, report = sys.argv[1], sys.argv[2]
apply = '--apply' in sys.argv
SRC = 'From_E' + BS + 'MediaRecovery' + BS + 'Videos' + BS
DST = 'Videos' + BS
ROOT = 'G:' + BS

db = sqlite3.connect(db_path)
disk = db.execute("select id from disks where serial = '315E7430'").fetchone()[0]
stamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%S.%f')[:-3] + 'Z'
rows = db.execute('select id, path, size, mtime from files where disk_id = ? and missing = 0 and path like ?',
                  (disk, SRC.replace(BS, BS) + '%')).fetchall()
rows = [r for r in rows if r[1].lower().startswith(SRC.lower()) and BS not in r[1][len(SRC):]]
on_disk = {e.name.lower() for e in os.scandir(ROOT + SRC.rstrip(BS)) if e.is_file()}

counts, out = {}, []
for fid, rel, size, mtime in rows:
    name = rel[len(SRC):]
    src, dst = ROOT + rel, ROOT + DST + name
    epoch_ticks = 621355968000000000
    if not os.path.exists(src):
        status = 'skip: not there any more'
    elif os.path.getsize(src) != size or abs((os.stat(src).st_mtime_ns // 100 + epoch_ticks) - mtime) > 20_000_000:
        status = 'skip: changed since it was catalogued'
    elif os.path.exists(dst):
        status = 'skip: a file with this name is already in G:\\Videos'
    else:
        status = 'ok'
        if apply:
            os.rename(src, dst)   # same disk: instant, never overwrites
            db.execute('delete from files where disk_id = ? and path = ? and id <> ? and missing = 1', (disk, DST + name, fid))
            db.execute('update files set path = ?, last_seen = ? where id = ?', (DST + name, stamp, fid))
            status = 'moved'
    on_disk.discard(name.lower())
    counts[status] = counts.get(status, 0) + 1
    out.append((status, src, dst, size))
if apply:
    db.commit()
with open(report, 'w', newline='', encoding='utf-8') as f:
    w = csv.writer(f); w.writerow(['status', 'from', 'to', 'size']); w.writerows(out)
print('APPLIED' if apply else 'DRY RUN', len(rows), counts, f'{sum(o[3] for o in out if o[0] in ("ok", "moved")) / 2**30:.1f} GB')
print('files in the folder that the catalog does not know (left alone):', sorted(on_disk)[:10], len(on_disk))
