"""Approved: 208 same-video copies -> G:\\duplicates\\Videos, then 65 review files -> G:\\review\\Videos.
Usage: python step_videos_final.py <repo> <scratch> [--apply]"""
import csv, json, os, sys

repo, scratch = sys.argv[1], sys.argv[2]
APPLY = '--apply' in sys.argv
sys.path.insert(0, os.path.join(repo, 'tools', 'media_cleanup'))
import media_cleanup as mc  # noqa: E402

J = os.path.join
G = 'G:' + os.sep
cat = mc.Catalog(J(repo, 'catalog.db'), G)

groups = json.load(open(J(scratch, 'videos-same-plan.json'), encoding='utf-8'))
dups = [mc.Move(m['path'], J(G, 'duplicates', 'Videos', os.path.basename(m['path'])), 'same video: ' + m['kind'],
                size=m['size'], mtime_ns=m['mtime_ns'], keep=g['keep']['path'], keep_size=g['keep']['size'])
        for g in groups for m in g['move']]
moving = {m.src.lower() for m in dups}
rows = list(csv.reader(open(J(scratch, 'review-videos.csv'), encoding='utf-8-sig'), delimiter=';'))[1:]
review = [mc.Move(J(G, 'Videos', r[0]), J(G, 'review', 'Videos', r[0]), r[2], size=int(r[1]))
          for r in rows if J(G, 'Videos', r[0]).lower() not in moving]
print('planned:', len(dups), 'duplicates,', len(review), 'review')

for name, moves in [('dup', dups), ('review', review)]:
    dry = mc.run_moves(moves, cat, J(scratch, f'final-{name}-dryrun.csv'), apply=False)
    if set(dry) != {'ok'}:
        sys.exit(f'{name}: STOPPED, dry run found {dict(dry)}')
    if APPLY:
        print(name, dict(mc.run_moves(moves, cat, J(scratch, f'final-{name}-applied.csv'), apply=True)))
    else:
        print(name, 'dry run ok')
