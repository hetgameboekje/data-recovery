namespace RecoveryCatalog;

/// <summary>
/// Totals per disk and overall: how much is unique, how much is a duplicate copy, and how much
/// still needs fingerprinting before that can be decided.
/// </summary>
static class Report
{
    // Classify every present, non-empty file once:
    //   unique  - no other file has the same content (unique size, unique quick hash, or unique full hash)
    //   dup     - identical content exists at least twice; rn = 1 is the copy counted as "the" content
    //   pending - shares a size/quick hash with another file but is not fingerprinted far enough yet
    const string Classified = """
        WITH f AS (
            SELECT id, disk_id, size, media_type, quick_hash, full_hash,
                   COUNT(*) OVER (PARTITION BY size) AS n_size,
                   COUNT(*) OVER (PARTITION BY size, quick_hash) AS n_quick
            FROM files WHERE missing = 0 AND size > 0),
        s AS (
            SELECT *, CASE
                WHEN n_size = 1 THEN 'unique'
                WHEN quick_hash IS NULL THEN 'pending'
                WHEN n_quick = 1 THEN 'unique'
                WHEN full_hash IS NULL THEN 'pending'
                ELSE 'hashed' END AS status
            FROM f),
        h AS (
            SELECT full_hash, COUNT(*) AS copies, COUNT(DISTINCT disk_id) AS on_disks
            FROM s WHERE status = 'hashed' GROUP BY full_hash),
        c AS (
            SELECT s.*, coalesce(h.copies, 1) AS copies, coalesce(h.on_disks, 1) AS on_disks,
                   CASE WHEN status = 'hashed' THEN ROW_NUMBER() OVER (PARTITION BY s.status, s.full_hash ORDER BY s.id) ELSE 1 END AS rn
            FROM s LEFT JOIN h ON s.status = 'hashed' AND h.full_hash = s.full_hash),
        k AS (
            SELECT id, disk_id, size, media_type, rn, on_disks,
                   CASE WHEN status = 'pending' THEN 'pending'
                        WHEN status = 'hashed' AND copies > 1 THEN 'dup'
                        ELSE 'unique' END AS kind
            FROM c)
        """;

    public static void Print(Catalog cat, int topGroups)
    {
        Console.WriteLine($"Catalog: {cat.FilePath}");
        Console.WriteLine();

        var sql = Classified;

        // ---- per disk
        var rows = new List<string[]>();
        var mounted = cat.MountedRoots();
        using (var c = cat.Cmd(sql + """
            SELECT d.id, coalesce(nullif(d.label, ''), '(no label)'), d.serial, coalesce(d.last_root, ''), coalesce(d.last_scan, 'never'),
                   COUNT(k.id), coalesce(SUM(k.size), 0),
                   coalesce(SUM(CASE WHEN k.kind = 'unique' THEN k.size END), 0),
                   coalesce(SUM(CASE WHEN k.kind = 'dup' THEN k.size END), 0),
                   coalesce(SUM(CASE WHEN k.kind = 'dup' AND k.on_disks > 1 THEN k.size END), 0),
                   coalesce(SUM(CASE WHEN k.kind = 'pending' THEN k.size END), 0),
                   (SELECT COUNT(*) FROM files e WHERE e.disk_id = d.id AND e.missing = 0 AND e.size = 0),
                   (SELECT COUNT(*) FROM files e WHERE e.disk_id = d.id AND e.missing = 0 AND e.error IS NOT NULL)
            FROM disks d LEFT JOIN k ON k.disk_id = d.id
            GROUP BY d.id ORDER BY d.id
            """))
        using (var r = c.ExecuteReader())
        {
            while (r.Read())
            {
                var id = r.GetInt64(0);
                rows.Add([
                    $"{r.GetString(1)} [{r.GetString(2)}]",
                    mounted.TryGetValue(id, out var root) ? root : "unplugged",
                    r.GetString(4).Length >= 16 ? r.GetString(4)[..16].Replace('T', ' ') : r.GetString(4),
                    $"{r.GetInt64(5):N0}",
                    Fmt.Bytes(r.GetInt64(6)),
                    Fmt.Bytes(r.GetInt64(7)),
                    Fmt.Bytes(r.GetInt64(8)),
                    Fmt.Bytes(r.GetInt64(9)),
                    Fmt.Bytes(r.GetInt64(10)),
                    $"{r.GetInt64(11):N0}",
                    $"{r.GetInt64(12):N0}",
                ]);
            }
        }
        Console.WriteLine("Per disk");
        Table(["Disk", "Now on", "Last scan (UTC)", "Files", "Total", "Only copy", "Duplicated", "..also on other disk", "Not yet decided", "Empty", "Read errors"], rows);
        Console.WriteLine("  Only copy  = content found nowhere else.  Duplicated = the same content exists more than once (here or on another disk).");
        Console.WriteLine();

        // ---- overall, by media type
        rows.Clear();
        long tFiles = 0, tTotal = 0, tUnique = 0, tRedundant = 0, tPending = 0;
        using (var c = cat.Cmd(sql + """
            SELECT media_type, COUNT(*), SUM(size),
                   coalesce(SUM(CASE WHEN kind = 'unique' OR (kind = 'dup' AND rn = 1) THEN size END), 0),
                   coalesce(SUM(CASE WHEN kind = 'dup' AND rn > 1 THEN size END), 0),
                   coalesce(SUM(CASE WHEN kind = 'pending' THEN size END), 0)
            FROM k GROUP BY media_type ORDER BY SUM(size) DESC
            """))
        using (var r = c.ExecuteReader())
        {
            while (r.Read())
            {
                long files = r.GetInt64(1), total = r.GetInt64(2), uniq = r.GetInt64(3), red = r.GetInt64(4), pend = r.GetInt64(5);
                tFiles += files; tTotal += total; tUnique += uniq; tRedundant += red; tPending += pend;
                rows.Add([r.GetString(0), $"{files:N0}", Fmt.Bytes(total), Fmt.Bytes(uniq), Fmt.Bytes(red), Fmt.Bytes(pend)]);
            }
        }
        rows.Add(["TOTAL", $"{tFiles:N0}", Fmt.Bytes(tTotal), Fmt.Bytes(tUnique), Fmt.Bytes(tRedundant), Fmt.Bytes(tPending)]);
        Console.WriteLine("All disks together");
        Table(["Type", "Files", "Total", "Unique data", "Extra copies", "Not yet decided"], rows);
        Console.WriteLine($"  Space needed to keep one copy of everything: {Fmt.Bytes(tUnique)}"
            + (tPending > 0 ? $" to {Fmt.Bytes(tUnique + tPending)} (depends on the {Fmt.Bytes(tPending)} not yet fingerprinted)" : ""));

        var pendingDisks = PendingUnplugged(cat, mounted);
        if (pendingDisks.Count > 0)
            Console.WriteLine($"  To finish fingerprinting, plug in and run 'rtcatalog hash': {string.Join(", ", pendingDisks)}");
        else if (tPending > 0)
            Console.WriteLine("  Run 'rtcatalog hash' to fingerprint the rest.");
        Console.WriteLine();

        if (topGroups > 0) TopGroups(cat, topGroups);
    }

    static List<string> PendingUnplugged(Catalog cat, Dictionary<long, string> mounted)
    {
        var list = new List<string>();
        using var c = cat.Cmd(Classified + "SELECT DISTINCT disk_id FROM k WHERE kind = 'pending'");
        using var r = c.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetInt64(0);
            if (!mounted.ContainsKey(id)) list.Add(cat.DiskName(id));
        }
        return list;
    }

    static void TopGroups(Catalog cat, int n)
    {
        Console.WriteLine($"Largest duplicate groups (top {n} by space taken by extra copies)");
        var groups = new List<(byte[] hash, long size, long copies)>();
        using (var c = cat.Cmd("""
            SELECT full_hash, size, COUNT(*) AS copies FROM files
            WHERE missing = 0 AND size > 0 AND full_hash IS NOT NULL
            GROUP BY full_hash HAVING COUNT(*) > 1
            ORDER BY size * (COUNT(*) - 1) DESC LIMIT $n
            """, null, ("$n", n)))
        using (var r = c.ExecuteReader())
            while (r.Read()) groups.Add(((byte[])r[0], r.GetInt64(1), r.GetInt64(2)));

        if (groups.Count == 0) { Console.WriteLine("  (none found yet)"); Console.WriteLine(); return; }
        foreach (var (hash, size, copies) in groups)
        {
            Console.WriteLine($"  {copies} x {Fmt.Bytes(size)}  (extra copies: {Fmt.Bytes(size * (copies - 1))})  {Fmt.Hex(hash)}");
            using var c = cat.Cmd("""
                SELECT coalesce(d.last_root, '?'), coalesce(nullif(d.label, ''), d.serial), f.path, f.mtime
                FROM files f JOIN disks d ON d.id = f.disk_id
                WHERE f.full_hash = $h AND f.missing = 0 ORDER BY f.mtime DESC
                """, null, ("$h", hash));
            using var r = c.ExecuteReader();
            while (r.Read())
                Console.WriteLine($"      {new DateTime(r.GetInt64(3), DateTimeKind.Utc).ToLocalTime():yyyy-MM-dd HH:mm}  [{r.GetString(1)}] {r.GetString(0)}{r.GetString(2)}");
        }
        Console.WriteLine();
    }

    public static void Disks(Catalog cat)
    {
        var mounted = cat.MountedRoots();
        var rows = new List<string[]>();
        using (var c = cat.Cmd("""
            SELECT d.id, d.serial, coalesce(nullif(d.label, ''), '(no label)'), coalesce(d.fs, ''), d.total_bytes,
                   coalesce(d.last_root, ''), coalesce(d.last_scan, 'never'),
                   (SELECT COUNT(*) FROM files f WHERE f.disk_id = d.id AND f.missing = 0)
            FROM disks d ORDER BY d.id
            """))
        using (var r = c.ExecuteReader())
            while (r.Read())
                rows.Add([$"{r.GetInt64(0)}", r.GetString(2), r.GetString(1), r.GetString(3), Fmt.Bytes(r.GetInt64(4)),
                          mounted.TryGetValue(r.GetInt64(0), out var root) ? root : "unplugged", r.GetString(6), $"{r.GetInt64(7):N0}"]);
        Console.WriteLine("Catalogued disks");
        Table(["#", "Label", "Serial", "FS", "Size", "Now on", "Last complete scan (UTC)", "Files"], rows);
        Console.WriteLine();

        var known = new HashSet<string>(rows.Select(r => r[2]));
        rows.Clear();
        foreach (var v in Volumes.Mounted())
            rows.Add([v.Root, v.Label, v.Serial, v.FileSystem, Fmt.Bytes(v.TotalBytes), known.Contains(v.Serial) ? "yes" : "no"]);
        Console.WriteLine("Plugged in now");
        Table(["Drive", "Label", "Serial", "FS", "Size", "In catalog"], rows);
    }

    static void Table(string[] head, List<string[]> rows)
    {
        var w = head.Select((h, i) => Math.Max(h.Length, rows.Count == 0 ? 0 : rows.Max(r => r[i].Length))).ToArray();
        string Line(string[] cells) => "  " + string.Join("  ", cells.Select((s, i) => i == 0 ? s.PadRight(w[i]) : s.PadLeft(w[i])));
        Console.WriteLine(Line(head));
        Console.WriteLine("  " + string.Join("  ", w.Select(n => new string('-', n))));
        foreach (var r in rows) Console.WriteLine(Line(r));
    }
}
