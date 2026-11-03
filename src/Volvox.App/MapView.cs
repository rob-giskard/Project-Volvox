using System.Drawing.Drawing2D;
using Volvox.Sim;

namespace Volvox.App;

/// <summary>
/// Graph-paper world map. Static layers (paper, grid, terrain, contours) are cached into a bitmap
/// and only re-rendered when the view changes; everything alive is drawn on top each frame.
/// Wheel = zoom, drag = pan, double-click = fit.
/// </summary>
internal sealed class MapView : Control
{
    private readonly record struct Seg(V2 A, V2 B, bool Major);

    public Simulation? Sim { get; private set; }
    public bool ShowBeliefs { get; set; } = true;
    public bool ShowEffort { get; set; }
    public bool ShowTrail { get; set; } = true;
    public string StatusText { get; set; } = "";

    private double _zoom = 3;          // pixels per world unit
    private double _cx = 100, _cy = 100; // world point at the view centre
    private Bitmap? _bg;
    private (double z, double cx, double cy, int w, int h) _bgKey;
    private readonly List<Seg> _contours = new();
    private bool _dragging;
    private Point _dragLast;
    private V2? _hover;
    private V2? _marker;
    private DateTime _markerUntil;

    public MapView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Paper;
        Dock = DockStyle.Fill;
    }

    public void SetSimulation(Simulation sim)
    {
        Sim = sim;
        BuildContours();
        FitWorld();
    }

    public void FitWorld()
    {
        if (Sim == null) return;
        int s = Sim.World.Size;
        if (Width > 10 && Height > 10) _zoom = Math.Max(0.5, Math.Min(Width, Height) / (s + 16.0));
        _cx = _cy = s / 2.0;
        InvalidateBackground();
    }

    public void CenterOn(V2 p)
    {
        _cx = p.X;
        _cy = p.Y;
        if (_zoom < 4) _zoom = 4;
        _marker = p;
        _markerUntil = DateTime.Now.AddSeconds(2.5);
        InvalidateBackground();
    }

    private void InvalidateBackground()
    {
        _bgKey = default;
        Invalidate();
    }

    // ── Coordinates ──────────────────────────────────────────

    private PointF W2S(V2 p) => new((float)((p.X - _cx) * _zoom + Width / 2.0), (float)((p.Y - _cy) * _zoom + Height / 2.0));
    private PointF W2S(double x, double y) => W2S(new V2(x, y));
    private V2 S2W(Point p) => new((p.X - Width / 2.0) / _zoom + _cx, (p.Y - Height / 2.0) / _zoom + _cy);

    // ── Input ────────────────────────────────────────────────

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button is MouseButtons.Left or MouseButtons.Middle or MouseButtons.Right)
        {
            _dragging = true;
            _dragLast = e.Location;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            _cx -= (e.X - _dragLast.X) / _zoom;
            _cy -= (e.Y - _dragLast.Y) / _zoom;
            _dragLast = e.Location;
            InvalidateBackground();
        }
        _hover = S2W(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var before = S2W(e.Location);
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.2 : 1 / 1.2), 0.5, 40);
        _cx = before.X - (e.X - Width / 2.0) / _zoom;
        _cy = before.Y - (e.Y - Height / 2.0) / _zoom;
        InvalidateBackground();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        FitWorld();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        InvalidateBackground();
    }

    // ── Contours (marching squares, computed once per world) ─

    private void BuildContours()
    {
        _contours.Clear();
        if (Sim == null) return;
        var t = Sim.World.Terrain;
        if (t.MaxHeight < 2) return;
        var pts = new V2[4];

        for (double level = 2; level < t.MaxHeight; level += 2)
        {
            bool major = Math.Abs(level % 10) < 1e-9;
            double lv = level + 1e-7;
            for (int x = 0; x < t.Size; x++)
            for (int y = 0; y < t.Size; y++)
            {
                double h00 = t.Height[x, y], h10 = t.Height[x + 1, y], h11 = t.Height[x + 1, y + 1], h01 = t.Height[x, y + 1];
                double lo = Math.Min(Math.Min(h00, h10), Math.Min(h11, h01));
                double hi = Math.Max(Math.Max(h00, h10), Math.Max(h11, h01));
                if (lo > lv || hi < lv) continue;

                int n = 0;
                AddCrossing(pts, ref n, x, y, h00, x + 1, y, h10, lv);
                AddCrossing(pts, ref n, x + 1, y, h10, x + 1, y + 1, h11, lv);
                AddCrossing(pts, ref n, x + 1, y + 1, h11, x, y + 1, h01, lv);
                AddCrossing(pts, ref n, x, y + 1, h01, x, y, h00, lv);
                if (n >= 2) _contours.Add(new Seg(pts[0], pts[1], major));
                if (n == 4) _contours.Add(new Seg(pts[2], pts[3], major));
            }
        }
    }

    private static void AddCrossing(V2[] pts, ref int n, double xa, double ya, double ha, double xb, double yb, double hb, double lv)
    {
        if ((ha - lv) * (hb - lv) >= 0 || n >= 4) return;
        double f = (lv - ha) / (hb - ha);
        pts[n++] = new V2(xa + (xb - xa) * f, ya + (yb - ya) * f);
    }

    // ── Static layer ─────────────────────────────────────────

    private void EnsureBackground()
    {
        if (Sim == null || Width <= 0 || Height <= 0) return;
        var key = (_zoom, _cx, _cy, Width, Height);
        if (_bg != null && _bgKey.Equals(key)) return;
        _bg?.Dispose();
        _bg = new Bitmap(Width, Height);
        _bgKey = key;

        using var g = Graphics.FromImage(_bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Outside);

        var t = Sim.World.Terrain;
        int size = t.Size;
        var tl = S2W(new Point(0, 0));
        var br = S2W(new Point(Width, Height));
        double wx0 = Math.Max(0, tl.X), wy0 = Math.Max(0, tl.Y);
        double wx1 = Math.Min(size, br.X), wy1 = Math.Min(size, br.Y);
        if (wx1 <= wx0 || wy1 <= wy0) return;

        var a = W2S(wx0, wy0);
        var b = W2S(wx1, wy1);
        using (var paper = new SolidBrush(Theme.Paper))
            g.FillRectangle(paper, a.X, a.Y, b.X - a.X, b.Y - a.Y);

        // Terrain types: marsh stipple, thicket hatching.
        float cell = (float)_zoom;
        using (var marshFill = new SolidBrush(Theme.Alpha(Theme.Marsh, 26)))
        using (var marshDot = new SolidBrush(Theme.Alpha(Theme.Marsh, 150)))
        using (var thicketFill = new SolidBrush(Theme.Alpha(Theme.Thicket, 24)))
        using (var thicketPen = new Pen(Theme.Alpha(Theme.Thicket, 120), 1f))
        {
            for (int x = (int)wx0; x < Math.Min(size, (int)Math.Ceiling(wx1)); x++)
            for (int y = (int)wy0; y < Math.Min(size, (int)Math.Ceiling(wy1)); y++)
            {
                var type = t.Type[x, y];
                if (type == TerrainType.Open) continue;
                var p = W2S(x, y);
                if (type == TerrainType.Marsh)
                {
                    g.FillRectangle(marshFill, p.X, p.Y, cell, cell);
                    if (cell >= 3)
                    {
                        float d = Math.Max(1.2f, cell * 0.12f);
                        g.FillEllipse(marshDot, p.X + cell * 0.25f, p.Y + cell * 0.3f, d, d);
                        g.FillEllipse(marshDot, p.X + cell * 0.7f, p.Y + cell * 0.75f, d, d);
                    }
                }
                else
                {
                    g.FillRectangle(thicketFill, p.X, p.Y, cell, cell);
                    if (cell >= 3) g.DrawLine(thicketPen, p.X, p.Y + cell, p.X + cell, p.Y);
                }
            }
        }

        // Graph-paper grid.
        int[] steps = { 1, 2, 5, 10, 20, 50 };
        int minor = steps.FirstOrDefault(st => st * _zoom >= 6, 50);
        int major = minor < 10 ? 10 : minor * 5;
        using (var minorPen = new Pen(Theme.GridMinor, 1f))
        using (var majorPen = new Pen(Theme.GridMajor, 1f))
        {
            for (int x = (int)Math.Ceiling(wx0 / minor) * minor; x <= wx1; x += minor)
                g.DrawLine(x % major == 0 ? majorPen : minorPen, W2S(x, wy0), W2S(x, wy1));
            for (int y = (int)Math.Ceiling(wy0 / minor) * minor; y <= wy1; y += minor)
                g.DrawLine(y % major == 0 ? majorPen : minorPen, W2S(wx0, y), W2S(wx1, y));
        }

        // Contours.
        using (var cPen = new Pen(Theme.Alpha(Theme.Contour, 200), 1f))
        using (var cMajor = new Pen(Theme.ContourMajor, 1.4f))
        {
            foreach (var s in _contours)
            {
                if (Math.Max(s.A.X, s.B.X) < wx0 - 1 || Math.Min(s.A.X, s.B.X) > wx1 + 1) continue;
                if (Math.Max(s.A.Y, s.B.Y) < wy0 - 1 || Math.Min(s.A.Y, s.B.Y) > wy1 + 1) continue;
                g.DrawLine(s.Major ? cMajor : cPen, W2S(s.A), W2S(s.B));
            }
        }

        // World border + coordinate labels.
        using (var border = new Pen(Theme.InkSoft, 1f))
        {
            var o = W2S(0, 0);
            var e = W2S(size, size);
            g.DrawRectangle(border, o.X, o.Y, e.X - o.X, e.Y - o.Y);
        }
        int labelStep = major * (major * _zoom < 40 ? 2 : 1);
        float topY = Math.Max(2, W2S(0, 0).Y + 2);
        float leftX = Math.Max(2, W2S(0, 0).X + 2);
        using (var labelBrush = new SolidBrush(Theme.InkSoft))
        {
            for (int x = (int)Math.Ceiling(wx0 / labelStep) * labelStep; x <= wx1; x += labelStep)
                g.DrawString(x.ToString(), Theme.MonoSmall, labelBrush, W2S(x, 0).X + 2, topY);
            for (int y = (int)Math.Ceiling(wy0 / labelStep) * labelStep; y <= wy1; y += labelStep)
                if (y > 0) g.DrawString(y.ToString(), Theme.MonoSmall, labelBrush, leftX, W2S(0, y).Y + 1);
        }
    }

    // ── Live layer ───────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (Sim == null)
        {
            g.Clear(Theme.Paper);
            return;
        }
        EnsureBackground();
        if (_bg != null) g.DrawImageUnscaled(_bg, 0, 0);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var mind = Sim.Mind;
        var o = Sim.Organism;
        var effort = mind.Effort;
        float cellPx = (float)(effort.CellSize * _zoom);

        if (ShowEffort)
        {
            using var brush = new SolidBrush(Color.Empty);
            for (int i = 0; i < effort.Samples.Length; i++)
            {
                if (effort.Samples[i] == 0) continue;
                double ratio = effort.Cost[i] / effort.Guess;
                var c = effort.Center(i);
                var p = W2S(c.X - effort.CellSize / 2.0, c.Y - effort.CellSize / 2.0);
                if (ratio > 1.15)
                    brush.Color = Theme.Alpha(Theme.Accent, (int)Math.Min(150, 20 + 40 * (ratio - 1)));
                else
                    brush.Color = Theme.Alpha(Theme.Belief, 18);
                g.FillRectangle(brush, p.X, p.Y, cellPx, cellPx);
            }
        }

        if (ShowBeliefs)
        {
            // Fog over cells it has never seen: what exists vs. what it knows exists.
            using var fog = new SolidBrush(Theme.Alpha(Theme.Ink, 16));
            for (int i = 0; i < effort.Seen.Length; i++)
            {
                if (effort.Seen[i]) continue;
                var c = effort.Center(i);
                var p = W2S(c.X - effort.CellSize / 2.0, c.Y - effort.CellSize / 2.0);
                g.FillRectangle(fog, p.X, p.Y, cellPx, cellPx);
            }
        }

        // Objects (the truth).
        float r = (float)Math.Clamp(_zoom * 0.9, 3, 9);
        using (var labelBrush = new SolidBrush(Theme.InkSoft))
        using (var stockBack = new SolidBrush(Theme.Alpha(Theme.Ink, 30)))
        using (var stockFill = new SolidBrush(Theme.Alpha(Theme.Ink, 150)))
        {
            foreach (var obj in Sim.World.Objects)
            {
                var p = W2S(obj.Pos);
                if (p.X < -20 || p.Y < -20 || p.X > Width + 20 || p.Y > Height + 20) continue;
                Glyphs.Draw(g, obj.Kind, p, obj.IsSource ? r * 1.15f : r * 0.85f);
                if (obj.HasStock)
                {
                    float w = r * 2;
                    g.FillRectangle(stockBack, p.X - r, p.Y + r + 3, w, 2.5f);
                    g.FillRectangle(stockFill, p.X - r, p.Y + r + 3, (float)(w * obj.Stock / obj.MaxStock), 2.5f);
                }
                if ((obj.IsSource && _zoom >= 2.5) || _zoom >= 7)
                    g.DrawString(obj.Label, Theme.MonoSmall, labelBrush, p.X + r + 3, p.Y - 6);
            }
        }

        // Beliefs: where it thinks things are.
        if (ShowBeliefs)
        {
            using var pen = new Pen(Theme.Alpha(Theme.Belief, 190), 1.3f) { DashStyle = DashStyle.Dash };
            using var text = new SolidBrush(Theme.Belief);
            foreach (var loc in mind.Locations)
            {
                var p = W2S(loc.Pos);
                float br = r + 5;
                pen.Color = Theme.Alpha(Theme.Belief, (int)(60 + 140 * loc.Confidence));
                g.DrawEllipse(pen, p.X - br, p.Y - br, 2 * br, 2 * br);
                string known = mind.Describe(loc.Kind);
                if (_zoom >= 3.5 && known != "unknown" && known != "unclear")
                    g.DrawString(known, Theme.MonoSmall, text, p.X + br + 2, p.Y + 4);
            }
        }

        // Trail: older = fainter.
        if (ShowTrail && o.Trail.Count > 1)
        {
            var pts = o.Trail.Select(q => W2S(q)).ToArray();
            int n = pts.Length;
            int recent = Math.Min(n, 1500);
            int oldEnd = n - recent;
            if (oldEnd > 1)
            {
                using var oldPen = new Pen(Theme.Alpha(Theme.Accent, 45), 1f);
                g.DrawLines(oldPen, pts[..(oldEnd + 1)]);
            }
            const int buckets = 10;
            for (int bIdx = 0; bIdx < buckets; bIdx++)
            {
                int start = oldEnd + bIdx * recent / buckets;
                int end = Math.Min(n - 1, oldEnd + (bIdx + 1) * recent / buckets);
                if (end - start < 1) continue;
                using var pen = new Pen(Theme.Alpha(Theme.Accent, 60 + 170 * (bIdx + 1) / buckets), 1.5f);
                g.DrawLines(pen, pts[start..(end + 1)]);
            }
        }

        // Planned route and goal.
        if (o.Alive && mind.PathIndex < mind.Path.Count)
        {
            var route = new List<PointF> { W2S(o.Pos) };
            for (int i = mind.PathIndex; i < mind.Path.Count; i++) route.Add(W2S(mind.Path[i]));
            using var pen = new Pen(Theme.Alpha(Theme.Belief, 150), 1.2f) { DashStyle = DashStyle.Dot };
            if (route.Count > 1) g.DrawLines(pen, route.ToArray());
            var goal = route[^1];
            using var gp = new Pen(Theme.Alpha(Theme.Belief, 200), 1f);
            g.DrawLine(gp, goal.X - 5, goal.Y, goal.X + 5, goal.Y);
            g.DrawLine(gp, goal.X, goal.Y - 5, goal.X, goal.Y + 5);
        }

        // The organism.
        var op = W2S(o.Pos);
        if (o.Alive)
        {
            double senseR = Sim.World.Terrain.TypeAt(o.Pos) == TerrainType.Thicket ? Sim.Config.ThicketSenseRadius : Sim.Config.SenseRadius;
            float sr = (float)(senseR * _zoom);
            using (var ring = new Pen(Theme.Alpha(Theme.Accent, 80), 1f) { DashStyle = DashStyle.Dash })
                g.DrawEllipse(ring, op.X - sr, op.Y - sr, 2 * sr, 2 * sr);

            float bodyR = (float)Math.Max(4, _zoom * 0.6);
            using (var body = new SolidBrush(Theme.Accent))
                g.FillEllipse(body, op.X - bodyR, op.Y - bodyR, 2 * bodyR, 2 * bodyR);
            using (var head = new Pen(Theme.Ink, 1.5f))
                g.DrawLine(head, op, new PointF(op.X + (float)o.Heading.X * bodyR * 2, op.Y + (float)o.Heading.Y * bodyR * 2));
            if (o.Acting)
            {
                using var act = new Pen(Theme.Ink, 1f);
                g.DrawEllipse(act, op.X - bodyR - 3, op.Y - bodyR - 3, 2 * bodyR + 6, 2 * bodyR + 6);
            }
            if (o.Sleeping)
            {
                using var z = new SolidBrush(Theme.Ink);
                g.DrawString("z z", Theme.MonoBold, z, op.X + bodyR + 2, op.Y - bodyR - 12);
            }
        }
        else
        {
            using var dead = new Pen(Theme.Ink, 2f);
            g.DrawLine(dead, op.X - 6, op.Y - 6, op.X + 6, op.Y + 6);
            g.DrawLine(dead, op.X - 6, op.Y + 6, op.X + 6, op.Y - 6);
            using var txt = new SolidBrush(Theme.Ink);
            g.DrawString($"DECEASED — {o.CauseOfDeath}", Theme.MonoBold, txt, op.X + 10, op.Y - 8);
        }

        // Log locator.
        if (_marker is V2 m && DateTime.Now < _markerUntil)
        {
            var mp = W2S(m);
            double phase = (_markerUntil - DateTime.Now).TotalSeconds;
            float rr = 10 + (float)(6 * Math.Abs(Math.Sin(phase * 4)));
            using var pen = new Pen(Theme.Belief, 1.6f);
            g.DrawEllipse(pen, mp.X - rr, mp.Y - rr, 2 * rr, 2 * rr);
            g.DrawLine(pen, mp.X - rr - 6, mp.Y, mp.X - rr + 4, mp.Y);
            g.DrawLine(pen, mp.X + rr - 4, mp.Y, mp.X + rr + 6, mp.Y);
            g.DrawLine(pen, mp.X, mp.Y - rr - 6, mp.X, mp.Y - rr + 4);
            g.DrawLine(pen, mp.X, mp.Y + rr - 4, mp.X, mp.Y + rr + 6);
        }

        DrawReadouts(g);
    }

    private void DrawReadouts(Graphics g)
    {
        if (Sim == null) return;
        using var back = new SolidBrush(Theme.Alpha(Theme.Paper, 225));
        using var ink = new SolidBrush(Theme.Ink);
        using var soft = new SolidBrush(Theme.InkSoft);
        using var line = new Pen(Theme.Line, 1f);

        // Status (top-left).
        if (StatusText.Length > 0)
        {
            var sz = g.MeasureString(StatusText, Theme.MonoBold);
            g.FillRectangle(back, 8, 8, sz.Width + 12, sz.Height + 6);
            g.DrawRectangle(line, 8, 8, sz.Width + 12, sz.Height + 6);
            g.DrawString(StatusText, Theme.MonoBold, ink, 14, 11);
        }

        // Cursor probe (bottom-left).
        if (_hover is V2 h && h.X >= 0 && h.Y >= 0 && h.X <= Sim.World.Size && h.Y <= Sim.World.Size)
        {
            var t = Sim.World.Terrain;
            string probe = $"x {h.X,6:0.0}  y {h.Y,6:0.0}  h {t.HeightAt(h),4:0.0}  {Names.Of(t.TypeAt(h)),-7}  {Sim.World.AmbientAt(h),5:0.0}°";
            var sz = g.MeasureString(probe, Theme.Mono);
            float y = Height - sz.Height - 12;
            g.FillRectangle(back, 8, y, sz.Width + 12, sz.Height + 6);
            g.DrawRectangle(line, 8, y, sz.Width + 12, sz.Height + 6);
            g.DrawString(probe, Theme.Mono, soft, 14, y + 3);
        }

        // Legend (bottom-right).
        var items = new (ObjectKind k, string label)[]
        {
            (ObjectKind.BerryBush, "food"), (ObjectKind.Pool, "water"), (ObjectKind.Vent, "heat"),
            (ObjectKind.Hollow, "shelter"), (ObjectKind.Stone, "stone"), (ObjectKind.Fern, "fern"),
            (ObjectKind.Nightshade, "nightshade"),
        };
        float lw = 118, lh = items.Length * 15 + 40;
        float lx = Width - lw - 10, ly = Height - lh - 10;
        g.FillRectangle(back, lx, ly, lw, lh);
        g.DrawRectangle(line, lx, ly, lw, lh);
        for (int i = 0; i < items.Length; i++)
        {
            float yy = ly + 10 + i * 15;
            Glyphs.Draw(g, items[i].k, new PointF(lx + 12, yy), 4.5f);
            g.DrawString(items[i].label, Theme.MonoSmall, soft, lx + 24, yy - 6);
        }
        float yy2 = ly + 10 + items.Length * 15;
        using (var cp = new Pen(Theme.ContourMajor, 1.2f)) g.DrawLine(cp, lx + 6, yy2, lx + 18, yy2);
        g.DrawString("contour (2 u)", Theme.MonoSmall, soft, lx + 24, yy2 - 6);
        using (var bp = new Pen(Theme.Belief, 1.2f) { DashStyle = DashStyle.Dash }) g.DrawEllipse(bp, lx + 7, yy2 + 9, 10, 10);
        g.DrawString("remembered", Theme.MonoSmall, soft, lx + 24, yy2 + 8);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _bg?.Dispose();
        base.Dispose(disposing);
    }
}
