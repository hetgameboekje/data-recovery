"""READ-ONLY. Re-labels the comparison honestly and writes the review list G:-free (in the scratchpad).

  A  broken ending   : the file that would move has a broken ending; the one that stays is intact. Video identical.
  B  other metadata  : both look complete; only the metadata block at the very end differs (e.g. Lightroom tags). Video identical.
  C  tiny scrap      : not a real video (a few bytes); left out of this cleanup.
"""
import csv, json, struct, sys

BS = chr(92)
S = sys.argv[1]


def ending_intact(path, size):
    with open(path, 'rb') as f:
        pos, last = 0, None
        while pos + 8 <= size:
            f.seek(pos)
            n, kind = struct.unpack('>I4s', f.read(8))
            if n == 1:
                n = struct.unpack('>Q', f.read(8))[0]
            elif n == 0:
                n = size - pos
            if n < 8 or not kind.decode('latin-1').isprintable():
                return False
            last = (kind, pos, n)
            pos += n
        if pos != size or last is None:
            return False
        if last[0] == b'uuid':
            f.seek(max(last[1], size - 256))
            return b'<?xpacket end=' in f.read()
        return True


def tail_diff(a, b, size):
    start = max(0, size - 64 * 1024)
    with open(a, 'rb') as fa, open(b, 'rb') as fb:
        fa.seek(start); fb.seek(start)
        x, y = fa.read(), fb.read()
    return next((size - (start + i) for i in range(len(x)) if x[i] != y[i]), 0)


plan = json.load(open(S + '/videos-plan.json', encoding='utf-8'))
rows = []
for p in plan:
    name, keep = p['move'].split(BS)[-1], p['keep'].split(BS)[-1]
    if p['size'] <= 1024 * 1024:
        p['cat'] = 'C'; why = f"only {p['size']} bytes: not a real video"
    elif not ending_intact(p['move'], p['size']):
        p['cat'] = 'A'; why = 'ending is broken; the copy that stays is intact'
    else:
        p['cat'] = 'B'; why = 'both complete; only the metadata at the end differs'
    p['tail'] = tail_diff(p['move'], p['keep'], p['size'])
    rows.append([p['cat'], name, keep, f"{p['size'] / 2**20:.1f}", p['tail'], why])
rows.sort(key=lambda r: (r[0], r[2], r[1]))
with open(S + '/videos-review.csv', 'w', newline='', encoding='utf-8-sig') as f:
    w = csv.writer(f, delimiter=';')
    w.writerow(['group', 'would move to G:\\duplicates\\Videos', 'stays in G:\\Videos', 'MB', 'bytes that differ (from the end)', 'why'])
    w.writerows(rows)
json.dump(plan, open(S + '/videos-plan.json', 'w', encoding='utf-8'), indent=1)
for c in 'ABC':
    sel = [p for p in plan if p['cat'] == c]
    print(c, len(sel), f"{sum(p['size'] for p in sel) / 2**30:.1f} GB", 'max differing bytes:', max((p['tail'] for p in sel), default=0))
