import sqlite3, sys, collections
BS = chr(92)
db = sqlite3.connect(f'file:{sys.argv[1]}?mode=ro', uri=True)
rows = db.execute('select id, disk_id, path, size, full_hash from files where missing = 0 and size > 0 and full_hash is not null').fetchall()
groups = collections.defaultdict(list)
for r in rows: groups[(r[4], r[3])].append(r)
inside = lambda r: r[1] == 2 and r[2].lower().startswith('from_e' + BS)
extras = 0; size = 0; kept_out = collections.Counter(); only_inside = 0; touched_outside = 0
for copies in groups.values():
    if len(copies) < 2: continue
    ins = [c for c in copies if inside(c)]
    if not ins: continue
    outs = [c for c in copies if not inside(c)]
    if outs:
        go = ins
        k = outs[0]; kept_out[(k[1], k[2].split(BS)[0])] += 1
    else:
        go = sorted(ins)[1:]; only_inside += 1
    extras += len(go); size += sum(c[3] for c in go)
print(f'extra copies inside G:\\From_E that can go: {extras:,} ({size/2**30:.1f} GB)')
print('groups where the copy that stays is outside From_E:', sum(kept_out.values()), ' e.g.', kept_out.most_common(8))
print('groups that exist only inside From_E (one copy stays there):', only_inside)
