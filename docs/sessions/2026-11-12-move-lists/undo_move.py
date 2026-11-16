"""Undo the 21:08 "Moving 15.312 files to G:\\process" run: rename every moved file back and point its catalog row back.

Usage: python undo_move.py <toolkit log> <catalog.db> <report.csv> [--apply]
Without --apply it only checks (dry run). Same volume, so every move back is a rename (instant, no copy).
"""
import csv, os, re, sqlite3, sys, datetime

BS = chr(92)
log_path, db_path, report_path = sys.argv[1:4]
apply = '--apply' in sys.argv

lines = open(log_path, encoding='utf-8', errors='replace').read().splitlines()
start = [i for i, l in enumerate(lines) if 'Moving 15.312 files' in l and 'G:' + BS + 'process' in l][-1]
moves = []
for l in lines[start:]:
    m = re.search(r'Moved \(([^)]*)\): (.+?) -> (.+)$', l)
    if m:
        moves.append((m.group(2), m.group(3)))
    if re.search(r'  Moved [\d.]+ files', l):
        break

db = sqlite3.connect(db_path)
disk_id = db.execute("select id from disks where serial = '315E7430'").fetchone()[0]
stamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%S.%f')[:-3] + 'Z'
counts = {}
rows = []
for src, dst in moves:
    src_rel, dst_rel = src[3:], dst[3:]
    if not os.path.exists(dst):
        status = 'skip: not in G:\\process any more'
    elif os.path.exists(src):
        status = 'skip: something is already at the original path'
    else:
        row = db.execute('select id, size from files where disk_id = ? and path = ?', (disk_id, dst_rel)).fetchone()
        if row is None:
            status = 'skip: not in the catalog'
        elif os.path.getsize(dst) != row[1]:
            status = 'skip: size differs from the catalog'
        else:
            status = 'ok'
            if apply:
                os.makedirs(os.path.dirname(src), exist_ok=True)
                os.rename(dst, src)   # same volume: a rename, never overwrites
                db.execute('delete from files where disk_id = ? and path = ? and id <> ? and missing = 1', (disk_id, src_rel, row[0]))
                db.execute('update files set path = ?, missing = 0, last_seen = ? where id = ?', (src_rel, stamp, row[0]))
                status = 'moved back'
    counts[status] = counts.get(status, 0) + 1
    rows.append((status, dst, src))
if apply:
    db.commit()
    # remove the now-empty folders under G:\process (only empty ones)
    for root, dirs, files in os.walk('G:' + BS + 'process', topdown=False):
        if not os.listdir(root):
            os.rmdir(root)

with open(report_path, 'w', newline='', encoding='utf-8') as f:
    w = csv.writer(f)
    w.writerow(['status', 'from', 'to'])
    w.writerows(rows)
print('APPLIED' if apply else 'DRY RUN', len(moves), counts)
