using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using RecoveryBLL.Dtos;
using RecoveryBLL.Models;
using RecoveryBLL.Services;

namespace RecoveryUI.Views.Controls;

/// <summary>
/// Draws a <see cref="MapNode"/> tree as a cushion treemap: every file is a block sized by its bytes and coloured by its type.
/// Striped blocks are mostly extra copies; a dotted edge means not fingerprinted yet. Read-only: it only reports clicks.
/// </summary>
public sealed class TreemapControl : Control
{
    /// <summary>The block under the mouse changed (null = none).</summary>
    public event EventHandler<MapNode?>? HoverChanged;
    public event EventHandler<MapNode>? NodeClicked;
    public event EventHandler<MapNode>? NodeDoubleClicked;

    // Light from the top left, as in WinDirStat; ambient light keeps shadows from going black.
    const double Ambient = 0.22, Diffuse = 0.88;
    static readonly double Lx, Ly, Lz;
    static TreemapControl()
    {
        double len = Math.Sqrt(1 + 1 + 10 * 10);
        (Lx, Ly, Lz) = (-1 / len, -1 / len, 10 / len);
    }

    MapNode? root;
    Color[] colors = [];
    string? highlightExt;
    MapNode? selected, hovered;
    TreemapResult? layout;
    Bitmap? image;
    readonly System.Windows.Forms.Timer resizeTimer = new() { Interval = 150 };

    public TreemapControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        resizeTimer.Tick += (_, _) => { resizeTimer.Stop(); Rebuild(); };
    }

    public MapNode? Root => root;

    public void SetMap(MapNode? newRoot, Color[] categoryColors)
    {
        root = newRoot;
        colors = categoryColors;
        selected = hovered = null;
        Rebuild();
    }

    /// <summary>Only files with this extension keep their colour; the rest is greyed out. Null shows everything.</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string? HighlightExtension
    {
        get => highlightExt;
        set { highlightExt = value; Render(); Invalidate(); }
    }

    /// <summary>Outlined block (a folder selected in the tree, or the block that was clicked).</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public MapNode? Selected
    {
        get => selected;
        set { selected = value; Invalidate(); }
    }

    /// <summary>The map as a picture (Expert: "Save as PNG").</summary>
    public Bitmap? Snapshot() => image == null ? null : new Bitmap(image);

    public MapNode? NodeAt(Point p) => layout?.HitTest(p.X + 0.5, p.Y + 0.5);

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        resizeTimer.Stop();
        resizeTimer.Start();   // rebuild once the user stops dragging
    }

    void Rebuild()
    {
        var size = ClientSize;
        layout = root == null || size.Width < 4 || size.Height < 4 ? null
            : TreemapLayout.Layout(root, new MapRect(0, 0, size.Width, size.Height), minSide: 2);
        Render();
        Invalidate();
    }

    void Render()
    {
        image?.Dispose();
        image = null;
        if (layout == null) return;
        int w = ClientSize.Width, h = ClientSize.Height;
        if (w < 1 || h < 1) return;
        var pixels = new int[w * h];
        Array.Fill(pixels, BackColor.ToArgb());

        foreach (var leaf in layout.Leaves)
        {
            var r = leaf.Rect;
            int x0 = Math.Clamp((int)Math.Round(r.X), 0, w), x1 = Math.Clamp((int)Math.Round(r.X + r.W), 0, w);
            int y0 = Math.Clamp((int)Math.Round(r.Y), 0, h), y1 = Math.Clamp((int)Math.Round(r.Y + r.H), 0, h);
            if (x1 <= x0 || y1 <= y0) continue;

            var n = leaf.Node;
            var c = n.Category >= 0 && n.Category < colors.Length ? colors[n.Category] : Color.Gray;
            bool dim = highlightExt != null && !(n.Kind == MapNodeKind.File && n.Ext == highlightExt);
            bool striped = n.Bytes > 0 && (n.State == MapFileState.ExtraCopy || n.Kind != MapNodeKind.File && n.ExtraCopyBytes * 2 >= n.Bytes);
            bool dotted = n.Bytes > 0 && (n.State == MapFileState.Pending || n.Kind != MapNodeKind.File && n.PendingBytes * 2 >= n.Bytes);

            for (int y = y0; y < y1; y++)
            {
                double ny = -(2 * leaf.S2Y * (y + 0.5) + leaf.S1Y);
                int row = y * w;
                for (int x = x0; x < x1; x++)
                {
                    double nx = -(2 * leaf.S2X * (x + 0.5) + leaf.S1X);
                    double cos = (nx * Lx + ny * Ly + Lz) / Math.Sqrt(nx * nx + ny * ny + 1);
                    double light = Ambient + Diffuse * Math.Max(0, cos);
                    if (striped && (x + y) % 7 < 2) light *= 0.45;
                    double rr = c.R * light, gg = c.G * light, bb = c.B * light;
                    if (dim) { rr = rr * 0.25 + 178; gg = gg * 0.25 + 178; bb = bb * 0.25 + 178; }
                    if (dotted && (x == x0 || x == x1 - 1 || y == y0 || y == y1 - 1) && ((x + y) / 2 & 1) == 0)
                        rr = gg = bb = 255;
                    pixels[row + x] = unchecked((int)0xFF000000) | (Clamp(rr) << 16) | (Clamp(gg) << 8) | Clamp(bb);
                }
            }
        }

        image = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = image.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < h; y++)
                Marshal.Copy(pixels, y * w, data.Scan0 + y * data.Stride, w);
        }
        finally { image.UnlockBits(data); }
    }

    static int Clamp(double v) => v <= 0 ? 0 : v >= 255 ? 255 : (int)v;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (image == null)
        {
            g.Clear(BackColor);
            TextRenderer.DrawText(g, root == null ? "" : "Nothing to show here.", Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        g.DrawImageUnscaled(image, 0, 0);
        if (selected != null && layout != null && layout.Rects.TryGetValue(selected, out var r))
        {
            var rect = Rectangle.Round(new RectangleF((float)r.X, (float)r.Y, (float)r.W, (float)r.H));
            rect.Width = Math.Max(rect.Width - 1, 1);
            rect.Height = Math.Max(rect.Height - 1, 1);
            using var white = new Pen(Color.White, 3);
            g.DrawRectangle(white, rect);
            g.DrawRectangle(Pens.Black, rect);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var n = NodeAt(e.Location);
        if (n == hovered) return;
        hovered = n;
        HoverChanged?.Invoke(this, n);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hovered = null;
        HoverChanged?.Invoke(this, null);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        // Right-click selects too, so the menu acts on the block under the mouse.
        if (NodeAt(e.Location) is { } n && e.Button is MouseButtons.Left or MouseButtons.Right)
        {
            Selected = n;
            NodeClicked?.Invoke(this, n);
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && NodeAt(e.Location) is { } n) NodeDoubleClicked?.Invoke(this, n);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            resizeTimer.Dispose();
            image?.Dispose();
        }
        base.Dispose(disposing);
    }
}
