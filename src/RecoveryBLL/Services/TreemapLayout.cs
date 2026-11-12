using RecoveryBLL.Models;

namespace RecoveryBLL.Services;

public readonly record struct MapRect(double X, double Y, double W, double H)
{
    public bool Contains(double px, double py) => px >= X && px < X + W && py >= Y && py < Y + H;
    public double Area => W * H;
}

/// <summary>
/// One drawn block. The four numbers describe its "cushion" (van Wijk &amp; van de Wetering, 1999): a surface that bulges per
/// nesting level, so the light and shadow show the folder borders without drawing lines.
/// Height at (x, y) = -(S2X·x² + S1X·x) - (S2Y·y² + S1Y·y) + c; the drawer only needs the slope.
/// </summary>
public readonly record struct MapLeaf(MapNode Node, MapRect Rect, double S1X, double S2X, double S1Y, double S2Y);

public sealed class TreemapResult
{
    public required IReadOnlyList<MapLeaf> Leaves { get; init; }
    public required IReadOnlyDictionary<MapNode, MapRect> Rects { get; init; }
    public required MapNode Root { get; init; }

    /// <summary>The deepest block under a point (a file, a merged block, or a folder too small to split further).</summary>
    public MapNode? HitTest(double x, double y)
    {
        if (!Rects.TryGetValue(Root, out var r) || !r.Contains(x, y)) return null;
        var node = Root;
        while (true)
        {
            MapNode? next = null;
            foreach (var c in node.Children)
                if (Rects.TryGetValue(c, out var cr) && cr.Contains(x, y)) { next = c; break; }
            if (next == null) return node;
            node = next;
        }
    }
}

/// <summary>
/// Squarified treemap (Bruls, Huizing &amp; van Wijk, 2000): children are laid out in rows whose blocks stay as close to
/// square as possible, so sizes are easy to compare. Pure geometry: no drawing, no UI types.
/// </summary>
public static class TreemapLayout
{
    const double Height = 0.5, Falloff = 0.75;

    /// <param name="minSide">A block smaller than this (in pixels) on either side is not split further: it is drawn as one block.</param>
    public static TreemapResult Layout(MapNode root, MapRect bounds, double minSide = 2)
    {
        var leaves = new List<MapLeaf>();
        var rects = new Dictionary<MapNode, MapRect>(ReferenceEqualityComparer.Instance);
        if (root.Bytes > 0 && bounds.W >= 1 && bounds.H >= 1)
            Place(root, bounds, 0, 0, 0, 0, Height, minSide, leaves, rects);
        return new TreemapResult { Leaves = leaves, Rects = rects, Root = root };
    }

    static void Place(MapNode node, MapRect r, double s1x, double s2x, double s1y, double s2y, double h, double minSide,
                      List<MapLeaf> leaves, Dictionary<MapNode, MapRect> rects)
    {
        rects[node] = r;
        // Each nesting level adds a ridge in both directions.
        if (r.W > 0) { s1x += 4 * h * (r.X + r.X + r.W) / r.W; s2x -= 4 * h / r.W; }
        if (r.H > 0) { s1y += 4 * h * (r.Y + r.Y + r.H) / r.H; s2y -= 4 * h / r.H; }

        var kids = node.Children.Where(c => c.Bytes > 0).ToList();
        if (kids.Count == 0 || r.W < minSide * 2 && r.H < minSide * 2 || r.W < minSide || r.H < minSide)
        {
            leaves.Add(new MapLeaf(node, r, s1x, s2x, s1y, s2y));
            return;
        }
        kids.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
        foreach (var (child, cr) in Squarify(kids, r))
            Place(child, cr, s1x, s2x, s1y, s2y, h * Falloff, minSide, leaves, rects);
    }

    /// <summary>Children (largest first) into rows along the shorter side of what is left.</summary>
    internal static List<(MapNode Node, MapRect Rect)> Squarify(IReadOnlyList<MapNode> kids, MapRect r)
    {
        var result = new List<(MapNode, MapRect)>(kids.Count);
        double total = kids.Sum(k => (double)k.Bytes);
        if (total <= 0) return result;
        double scale = r.W * r.H / total;
        double x = r.X, y = r.Y, w = r.W, h = r.H;
        int i = 0;
        while (i < kids.Count)
        {
            double side = Math.Min(w, h);
            int j = i;
            double rowSum = 0, worst = double.MaxValue;
            while (j < kids.Count)
            {
                double area = kids[j].Bytes * scale;
                double sum = rowSum + area;
                // Sorted largest first: the row's largest block is its first, the smallest the one being added.
                double max = kids[i].Bytes * scale, min = area;
                double next = Math.Max(side * side * max / (sum * sum), sum * sum / (side * side * min));
                if (j > i && next > worst) break;
                worst = next;
                rowSum = sum;
                j++;
            }
            bool last = j >= kids.Count;
            if (w >= h)
            {
                // A column on the left, as wide as the row's area needs.
                double colW = last ? w : rowSum / h;
                double cy = y;
                for (int k = i; k < j; k++)
                {
                    double bh = k == j - 1 ? y + h - cy : kids[k].Bytes * scale / colW;
                    result.Add((kids[k], new MapRect(x, cy, colW, bh)));
                    cy += bh;
                }
                x += colW; w -= colW;
            }
            else
            {
                double rowH = last ? h : rowSum / w;
                double cx = x;
                for (int k = i; k < j; k++)
                {
                    double bw = k == j - 1 ? x + w - cx : kids[k].Bytes * scale / rowH;
                    result.Add((kids[k], new MapRect(cx, y, bw, rowH)));
                    cx += bw;
                }
                y += rowH; h -= rowH;
            }
            i = j;
        }
        return result;
    }
}
