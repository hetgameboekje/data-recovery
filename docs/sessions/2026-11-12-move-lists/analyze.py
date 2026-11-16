import sqlite3, re, sys, collections, os
S = sys.argv[1]; LOG = sys.argv[2]
BS = chr(92)
lines = open(LOG, encoding='utf-8', errors='replace').read().splitlines()
start = [i for i, l in enumerate(lines) if 'Moving 15.312 files' in l][-1]
moves = []
for l in lines[start:]:
    m = re.search(r'Moved \(([^)]*)\): (.+?) -> (.+)$', l)
    if m: moves.append((m.group(1), m.group(2), m.group(3)))
    if re.search(r'  Moved [\d.]+ files', l): break
print('moves', len(moves))
c = collections.Counter(BS.join(s.split(BS)[1:3]) for _, s, _ in moves)
print('source folders:', c.most_common(15))
db = sqlite3.connect(f'file:{S}/catalog.db?mode=ro', uri=True)
disks = {r[0]: (r[1], r[2], r[3]) for r in db.execute('select id,serial,label,last_root from disks')}
for k, v in disks.items(): print('disk', k, v)
gid = [k for k, v in disks.items() if (v[2] or '').upper().startswith('G:')]
print('G ids', gid)
kept = collections.Counter(); nokept = 0; notfound = 0; samples = []
for reason, src, dst in moves:
    rel = dst[3:]
    row = db.execute('select id,full_hash,size,missing,disk_id from files where path=? and disk_id in (%s)' % ','.join(map(str, gid)), (rel,)).fetchone()
    if not row: notfound += 1; continue
    others = db.execute('select disk_id,path,missing from files where full_hash=? and size=? and id<>?', (row[1], row[2], row[0])).fetchall()
    present = [o for o in others if o[2] == 0 and not o[1].lower().startswith('process' + BS)]
    if not present:
        nokept += 1
        if len(samples) < 5: samples.append((src, others[:3]))
        continue
    o = present[0]
    kept[(o[0], BS.join(o[1].split(BS)[:2]))] += 1
print('rows not found:', notfound)
print('where a copy still is (disk, folder):', kept.most_common(20))
print('moved files with NO other copy outside process:', nokept)
for s_ in samples: print('  ', s_)
