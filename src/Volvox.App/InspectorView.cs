using Volvox.Sim;

namespace Volvox.App;

/// <summary>Instrument-panel readout of the organism: body, current intent and why, what it knows.</summary>
internal sealed class InspectorView : Control
{
    public Simulation? Sim { get; set; }

    private const int Pad = 12;
    private const int LineH = 16;

    public InspectorView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Panel;
        Dock = DockStyle.Fill;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Panel);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        if (Sim == null) return;

        var o = Sim.Organism;
        var mind = Sim.Mind;
        using var ink = new SolidBrush(Theme.Ink);
        using var soft = new SolidBrush(Theme.InkSoft);
        using var accent = new SolidBrush(Theme.Accent);
        using var belief = new SolidBrush(Theme.Belief);
        using var rule = new Pen(Theme.Line, 1f);

        float y = Pad;
        float w = Width - 2 * Pad;

        void Put(string s, Brush b, float x = Pad, Font? f = null)
        {
            g.DrawString(s, f ?? Theme.Mono, b, x, y);
        }
        void NextLine(float factor = 1) => y += LineH * factor;
        void Rule()
        {
            y += 4;
            g.DrawLine(rule, Pad, y, Pad + w, y);
            y += 8;
        }
        void Header(string s)
        {
            Put(s, soft, Pad, Theme.MonoBold);
            NextLine(1.15f);
        }

        // ── Identity ─────────────────────────────────────────
        Put($"ORGANISM #{o.Id}", ink, Pad, Theme.MonoTitle);
        string state = !o.Alive ? "DECEASED" : o.Sleeping ? "SLEEPING" : o.Acting ? "ACTING" : "ACTIVE";
        var sz = g.MeasureString(state, Theme.MonoBold);
        g.DrawString(state, Theme.MonoBold, o.Alive ? ink : accent, Pad + w - sz.Width, y);
        NextLine(1.3f);
        Put($"age      T+{Names.Time(o.Age)}", soft); NextLine();
        var t = Sim.World.Terrain;
        Put($"pos      {o.Pos}   h {t.HeightAt(o.Pos):0.0}   {Names.Of(t.TypeAt(o.Pos))}", soft); NextLine();
        Put($"feels    {o.Ambient:0.0}°  damp {Sim.World.HumidityAt(o.Pos):0.00}  sweet {Sim.World.ScentAt(o.Pos):0.00}", soft); NextLine();
        Put($"walked   {o.Distance:0} u", soft); NextLine();
        if (!o.Alive) { Put($"cause    {o.CauseOfDeath}", accent); NextLine(); }
        Rule();

        // ── Body ─────────────────────────────────────────────
        Header("BODY");
        Bar(g, ref y, "ENERGY", o.Energy, 100, false);
        Bar(g, ref y, "  stomach", o.Stomach, Sim.Config.StomachCapacity, true);
        Bar(g, ref y, "HYDRATION", o.Hydration, 100, false);
        Bar(g, ref y, "WARMTH", o.Warmth, 100, false);
        Bar(g, ref y, "REST", o.Rest, 100, false);
        Bar(g, ref y, "HEALTH", o.Health, 100, false);
        Rule();

        // ── Mind ─────────────────────────────────────────────
        Header("INTENT");
        string intent = mind.Current.Type == IntentType.Idle ? "—" : mind.Current.Key;
        if (o.Acting) intent += $"  [{mind.ActionTicksLeft}s]";
        Put(Clip(g, intent, w), ink); NextLine(1.2f);
        Put("why:", soft); NextLine();
        foreach (var c in mind.LastScores)
        {
            bool chosen = mind.Current.Type != IntentType.Idle && c.Key == mind.Current.Key;
            Put($"{c.Score,5:0.00}  {Clip(g, c.Key, w - 60)}", chosen ? ink : soft, Pad + 6);
            NextLine();
        }
        Rule();

        Header("KNOWS");
        var kinds = mind.Associations.Select(a => a.Kind).Distinct().ToList();
        if (kinds.Count == 0) { Put("nothing yet", soft, Pad + 6); NextLine(); }
        foreach (var k in kinds)
        {
            Glyphs.Draw(g, k, new PointF(Pad + 10, y + 7), 4.5f);
            string desc = mind.Describe(k);
            var best = mind.Associations.Where(a => a.Kind == k && a.Useful).MaxBy(a => a.Confidence);
            string detail = best != null ? $"  {Names.Of(best.Action)} {best.Confidence:0.00}" : "";
            Put($"{Names.Of(k),-11}{desc}{detail}", desc == "harmful" ? accent : best != null ? belief : soft, Pad + 22);
            NextLine();
        }
        Rule();

        Header($"REMEMBERS {mind.Locations.Count} PLACES");
        var shown = mind.Locations
            .OrderByDescending(l => mind.Describe(l.Kind).EndsWith("source") ? 1 : 0)
            .ThenBy(l => V2.Dist(l.Pos, o.Pos))
            .ToList();
        foreach (var l in shown)
        {
            if (y > Height - LineH * 3) { Put($"… {shown.Count - shown.IndexOf(l)} more", soft, Pad + 6); NextLine(); break; }
            Glyphs.Draw(g, l.Kind, new PointF(Pad + 10, y + 7), 4f, (int)(80 + 175 * l.Confidence));
            string empty = l.EmptyUntil > Sim.Tick ? " empty" : "";
            Put($"{l.Label,-16}{l.Pos,-11}{l.Confidence,4:0.00}{empty}", soft, Pad + 22);
            NextLine();
        }
        Rule();

        var stats = Sim.Stats;
        Put($"tries {stats.Tries} · uses {stats.Uses} · seen {stats.ObjectsNoticed} · reroutes {stats.RouteSwitches}", soft);
    }

    private static string Clip(Graphics g, string s, float width)
    {
        if (g.MeasureString(s, Theme.Mono).Width <= width) return s;
        while (s.Length > 3 && g.MeasureString(s + "…", Theme.Mono).Width > width) s = s[..^1];
        return s + "…";
    }

    private void Bar(Graphics g, ref float y, string label, double value, double max, bool thin)
    {
        float labelW = 92, valueW = 34;
        float x = Pad + labelW;
        float bw = Width - 2 * Pad - labelW - valueW;
        float h = thin ? 4 : 8;
        float by = y + (LineH - h) / 2f - 1;
        double frac = max <= 0 ? 0 : Math.Clamp(value / max, 0, 1);
        bool critical = !thin && value < 20;

        using var lb = new SolidBrush(thin ? Theme.InkSoft : Theme.Ink);
        using var track = new SolidBrush(Theme.Alpha(Theme.Ink, 22));
        using var fill = new SolidBrush(critical ? Theme.Accent : thin ? Theme.InkSoft : Theme.Ink);
        using var tick = new Pen(Theme.Alpha(Theme.Ink, 50), 1f);
        using var crit = new SolidBrush(Theme.Accent);

        g.DrawString(label, thin ? Theme.MonoSmall : Theme.Mono, lb, Pad, y + (thin ? 1 : 0));
        g.FillRectangle(track, x, by, bw, h);
        g.FillRectangle(fill, x, by, (float)(bw * frac), h);
        for (int i = 1; i < 4; i++)
        {
            float tx = x + bw * i / 4f;
            g.DrawLine(tick, tx, by + h, tx, by + h + 2);
        }
        g.DrawString($"{value,3:0}", Theme.Mono, critical ? crit : lb, x + bw + 4, y);
        y += thin ? LineH - 3 : LineH + 1;
    }
}
