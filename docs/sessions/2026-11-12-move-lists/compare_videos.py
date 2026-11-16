"""READ-ONLY. Finds duplicates inside G:\\Videos (top level only) and writes a review list. Moves nothing.

  exact          : same size, same SHA-256 of the whole file
  damaged ending : same size, identical except somewhere in the last 64 KB
Per group, the copy that stays: the one with an intact ending first, then the lowest number in the name.

Usage: python compare_videos.py <out.csv> <out.json>
"""
import csv, hashlib, json, os, re, struct, sys, time, collections

BS = chr(92)
FOLDER = 'G:' + BS + 'Videos'
TAIL = 64 * 1024
CHUNK = 8 * 1024 * 1024
out_csv, out_json = sys.argv[1], sys.argv[2]


def ending_intact(path, size):
    """True/False for MP4/MOV whose top-level boxes we can read; None when we cannot tell."""
    try:
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
                    return False if pos > 0 else None
                last = (kind, pos, n)
                pos += n
            if pos != size or last is None:
                return False
            kind, start, n = last
            if kind == b'uuid':                      # XMP metadata block: must end with its closing tag
                f.seek(max(start, size - 256))
                return b'<?xpacket end=' in f.read()
            return True
    except OSError:
        return None


def number(name):
    m = re.search(r'\d+', name)
    return (int(m.group()) if m else 1 << 62, name.lower())


files = [e for e in os.scandir(FOLDER) if e.is_file()]
by_size = collections.defaultdict(list)
for e in files:
    by_size[e.stat().st_size].append(e)
groups = [g for g in by_size.values() if len(g) > 1]
total = sum(e.stat().st_size for g in groups for e in g)
print(f'{len(files)} files in {FOLDER}; {len(groups)} same-size groups, {sum(map(len, groups))} files, {total / 2**30:.1f} GB to read', flush=True)

info, done, t0 = {}, 0, time.time()
for g in groups:
    for e in g:
        size = e.stat().st_size
        full = hashlib.sha256()
        cut = max(0, size - TAIL)
        read = 0
        with open(e.path, 'rb') as f:
            while read < cut:                                  # everything except the last 64 KB
                b = f.read(min(CHUNK, cut - read))
                if not b:
                    break
                full.update(b)
                read += len(b)
            head_digest = full.copy().hexdigest()
            while b := f.read(CHUNK):                          # the last 64 KB
                full.update(b)
        st = os.stat(e.path)
        info[e.path] = dict(name=e.name, size=size, mtime_ns=st.st_mtime_ns, full=full.hexdigest(), head=head_digest,
                            intact=ending_intact(e.path, size))
        done += size
        el = time.time() - t0
        print(f'  {done / 2**30:6.1f} / {total / 2**30:.1f} GB  {done / max(el, .1) / 2**20:5.0f} MB/s  {e.name}', flush=True)

rows, plan = [], []
for g in groups:
    by_head = collections.defaultdict(list)
    for e in g:
        by_head[info[e.path]['head']].append(info[e.path] | {'path': e.path})
    for cluster in by_head.values():
        if len(cluster) < 2:
            continue
        cluster.sort(key=lambda x: (0 if x['intact'] is True else 1 if x['intact'] is None else 2, number(x['name'])))
        keep = cluster[0]
        for x in cluster[1:]:
            kind = 'exact' if x['full'] == keep['full'] else 'damaged ending'
            if kind == 'damaged ending' and keep['intact'] is not True:
                note = "can't tell which ending is intact; kept the lowest number"
            elif kind == 'damaged ending':
                note = 'kept file has the intact ending'
            else:
                note = 'byte-for-byte identical'
            target = 'G:' + BS + 'duplicates' + BS + 'Videos' + BS + x['name']
            rows.append([x['name'], keep['name'], kind, f"{x['size'] / 2**20:.1f}", note, x['path'], target])
            plan.append(dict(move=x['path'], to=target, size=x['size'], mtime_ns=x['mtime_ns'], full=x['full'],
                             keep=keep['path'], keep_size=keep['size'], keep_mtime_ns=keep['mtime_ns'], kind=kind))

rows.sort(key=lambda r: (r[2], r[1]))
with open(out_csv, 'w', newline='', encoding='utf-8-sig') as f:
    w = csv.writer(f, delimiter=';')
    w.writerow(['moves to G:\\duplicates\\Videos', 'stays in G:\\Videos', 'kind', 'MB', 'why', 'from', 'to'])
    w.writerows(rows)
json.dump(plan, open(out_json, 'w', encoding='utf-8'), indent=1)
kinds = collections.Counter(r[2] for r in rows)
print('RESULT', dict(kinds), f'{sum(p["size"] for p in plan) / 2**30:.1f} GB would move;',
      'unsure which ending is intact:', sum(1 for r in rows if r[4].startswith("can't")))
