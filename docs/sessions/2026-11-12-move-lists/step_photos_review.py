import csv, os, sys
repo, scratch = sys.argv[1], sys.argv[2]
APPLY = '--apply' in sys.argv
sys.path.insert(0, os.path.join(repo, 'tools', 'media_cleanup'))
import media_cleanup as mc
J = os.path.join
G = 'G:' + os.sep
cat = mc.Catalog(J(repo, 'catalog.db'), G)
rows = list(csv.reader(open(J(scratch, 'review-photos.csv'), encoding='utf-8-sig'), delimiter=';'))[1:]
moves = [mc.Move(J(G, 'Photos', r[0]), J(G, 'review', 'Photos', r[0]), r[2][:80], size=int(r[1])) for r in rows]
dry = mc.run_moves(moves, cat, J(scratch, 'photos-review-dryrun.csv'), apply=False)
if set(dry) != {'ok'}:
    sys.exit(f'STOPPED, dry run found {dict(dry)}')
print('dry run ok', len(moves))
if APPLY:
    print(dict(mc.run_moves(moves, cat, J(scratch, 'photos-review-applied.csv'), apply=True)))
