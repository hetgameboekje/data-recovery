"""Steps 1-6 agreed with the user. Each step: build the list, dry run, stop on anything unexpected, then apply.

Usage: python steps_1_to_6.py <repo> <scratch> [--apply]
"""
import json, os, sys

repo, scratch = sys.argv[1], sys.argv[2]
APPLY = '--apply' in sys.argv
sys.path.insert(0, os.path.join(repo, 'tools', 'media_cleanup'))
import media_cleanup as mc  # noqa: E402

S = os.sep
G = 'G:' + S
J = lambda *p: os.path.join(*p)
FROM = J(G, 'From_E')
MR = J(FROM, 'MediaRecovery')
cat = mc.Catalog(J(repo, 'catalog.db'), G)


def files_in(folder, recursive=False):
    if not os.path.isdir(folder):
        return []
    if not recursive:
        return sorted(e.path for e in os.scandir(folder) if e.is_file())
    return sorted(J(d, f) for d, _, fs in os.walk(folder) for f in fs)


def master_copy(path, masters):
    """A catalogued copy with the same content that lies in one of the master folders (not in From_E/duplicates)."""
    row = cat.row(path)
    if row is None or row[2] is None:
        return None
    for p in cat.present_with_hash(row[2], row[1]):
        if p != path and any(p.lower().startswith(m.lower() + S) for m in masters):
            return p
    return None


steps = {}

# 1. G:\Videos: broken endings and other-metadata copies -> G:\duplicates\Videos (keeper recomputed: intact, most tags, lowest number)
plan = json.load(open(J(scratch, 'videos-plan.json'), encoding='utf-8'))
clusters = {}
for p in plan:
    if p.get('cat') == 'C':
        continue                                      # the 24-byte scrap: goes to review in step 8
    c = clusters.setdefault(p['keep'], {p['keep']: p['keep_mtime_ns']})
    c[p['move']] = p['mtime_ns']
s1 = []
for members in clusters.values():
    info = [(path, mc.mp4_ending_intact(path), mc.xmp_size(path)) for path in members]
    info.sort(key=lambda x: (0 if x[1] is True else 1 if x[1] is None else 2, -x[2], mc.name_number(os.path.basename(x[0]))))
    keep = info[0][0]
    for path, intact, tags in info[1:]:
        why = 'broken ending' if intact is False else 'same video, fewer tags'
        s1.append(mc.Move(path, J(G, 'duplicates', 'Videos', os.path.basename(path)), why,
                          mtime_ns=members[path], keep=keep, keep_size=os.path.getsize(keep)))
steps['1 Videos cleanup'] = s1

# 2. From_E _Duplicates\Videos -> G:\duplicates\Videos (each must have its exact copy in G:\Videos)
steps['2 From_E duplicate videos'] = [
    mc.Move(f, J(G, 'duplicates', 'Videos', os.path.basename(f)), 'exact copy of a video in G:\\Videos',
            keep=master_copy(f, [J(G, 'Videos')]) or 'MISSING')
    for f in files_in(J(MR, '_Duplicates', 'Videos'))]

# 3. From_E Photos -> G:\Photos
steps['3 From_E photos'] = [mc.Move(f, J(G, 'Photos', os.path.basename(f)), 'merge into master') for f in files_in(J(MR, 'Photos'))]

# 4. From_E _Duplicates\Photos -> G:\duplicates\Photos (exact copy must be in G:\Photos; checked at run time, after step 3)
dup_photos = files_in(J(MR, '_Duplicates', 'Photos'))

# 5. Audio / Other: into the master, or to duplicates when an exact copy is already in that master
def audio_other(sources, dry_masters=()):
    out = []
    for src_folder, kind in sources:
        for f in files_in(src_folder):
            m = master_copy(f, [J(G, kind), *dry_masters])
            if m:
                out.append(mc.Move(f, J(G, 'duplicates', kind, os.path.basename(f)), 'exact copy of ' + m, keep=m))
            else:
                out.append(mc.Move(f, J(G, kind, os.path.basename(f)), 'merge into master'))
    return out

# 6. MediaOverzicht: preview images -> G:\previews, page files -> G:\previews\old-overview-page; From_E logs -> G:\_RecoveryToolkit\From_E-logs
MO = J(FROM, 'MediaOverzicht')
s6 = [mc.Move(f, J(G, 'previews', os.path.basename(f)), 'preview image') for f in files_in(J(MO, 'miniaturen'))]
s6 += [mc.Move(f, J(G, 'previews', 'old-overview-page', os.path.relpath(f, MO)), 'old overview page')
       for f in files_in(MO, recursive=True) if not f.lower().startswith(J(MO, 'miniaturen').lower() + S)]
s6 += [mc.Move(f, J(G, '_RecoveryToolkit', 'From_E-logs', os.path.basename(f)), 'log of the earlier recovery')
       for f in [J(MR, 'recovery-log.txt')] + files_in(J(MR, '_Duplicates'))]


def run(name, moves):
    if not moves:
        print(f'{name}: nothing to do'); return True
    tag = name.split()[0]
    dry = mc.run_moves(moves, cat, J(scratch, f'step{tag}-dryrun.csv'), apply=False)
    bad = {k: v for k, v in dry.items() if k != 'ok'}
    if bad:
        print(f'{name}: STOPPED, dry run found {bad} (see step{tag}-dryrun.csv)'); return False
    if not APPLY:
        print(f'{name}: dry run ok, {len(moves)} files'); return True
    done = mc.run_moves(moves, cat, J(scratch, f'step{tag}-applied.csv'), apply=True)
    gb = sum(os.path.getsize(m.dst if os.path.exists(m.dst) else m.src) for m in moves if os.path.exists(m.dst) or os.path.exists(m.src)) / 2**30
    print(f'{name}: {dict(done)}  (~{gb:.1f} GB)')
    return set(done) == {'moved'}


ok = run('1 Videos cleanup', s1) and run('2 From_E duplicate videos', steps['2 From_E duplicate videos']) \
    and run('3 From_E photos', steps['3 From_E photos'])
if ok:
    s4 = [mc.Move(f, J(G, 'duplicates', 'Photos', os.path.basename(f)), 'exact copy of a photo in G:\\Photos',
                  keep=master_copy(f, [J(G, 'Photos')]) or 'MISSING') for f in dup_photos]
    if not APPLY:
        # before step 3 really ran, the master copies are still in From_E\MediaRecovery\Photos: accept those in the dry run
        s4 = [mc.Move(m.src, m.dst, m.why, keep=m.keep if m.keep != 'MISSING' else (master_copy(m.src, [J(MR, 'Photos')]) or 'MISSING'))
              for m in s4]
    ok = run('4 From_E duplicate photos', s4) \
        and run('5a Audio and Other', audio_other([(J(MR, 'Audio'), 'Audio'), (J(MR, 'Other'), 'Other')])) \
        and run('5b From_E duplicate Other', audio_other([(J(MR, '_Duplicates', 'Other'), 'Other')],
                                                         () if APPLY else (J(MR, 'Other'),))) \
        and run('6 Previews and logs', s6)
if ok and APPLY:
    print('empty folders removed in From_E:', mc.remove_empty_folders(FROM))
    print('From_E still exists:', os.path.exists(FROM))
