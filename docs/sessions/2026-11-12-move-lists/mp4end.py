"""Walk the top-level MP4 boxes and report where the real video ends (read-only)."""
import os, struct, sys

for path in sys.argv[1:]:
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
            name = kind.decode('latin-1')
            if n < 8 or not name.isprintable():
                break
            boxes.append((name, pos, n))
            pos += n
        f.seek(size - 64)
        tail = f.read(64)
    print(os.path.basename(path), 'size', size)
    print('  boxes:', [(b[0], b[1], b[2]) for b in boxes])
    print('  boxes end at', pos, '-> bytes after the last box:', size - pos)
    print('  last 64 bytes:', tail.hex())
