using Volvox.Sim;

namespace Volvox.App;

/// <summary>Engineering-paper look: warm off-white, pale green grid, dark ink, one orange accent.</summary>
internal static class Theme
{
    public static readonly Color Paper = Color.FromArgb(246, 244, 236);
    public static readonly Color PaperAlt = Color.FromArgb(240, 238, 229);
    public static readonly Color Outside = Color.FromArgb(226, 224, 215);
    public static readonly Color Panel = Color.FromArgb(234, 232, 224);
    public static readonly Color Line = Color.FromArgb(200, 197, 186);
    public static readonly Color GridMinor = Color.FromArgb(222, 233, 227);
    public static readonly Color GridMajor = Color.FromArgb(180, 207, 196);
    public static readonly Color Ink = Color.FromArgb(36, 46, 52);
    public static readonly Color InkSoft = Color.FromArgb(108, 118, 122);
    public static readonly Color InkFaint = Color.FromArgb(160, 166, 166);
    public static readonly Color Accent = Color.FromArgb(214, 72, 22);
    public static readonly Color Belief = Color.FromArgb(30, 104, 196);
    public static readonly Color Select = Color.FromArgb(212, 225, 240);
    public static readonly Color Hover = Color.FromArgb(224, 222, 212);
    public static readonly Color Contour = Color.FromArgb(190, 162, 128);
    public static readonly Color ContourMajor = Color.FromArgb(156, 124, 90);
    public static readonly Color Marsh = Color.FromArgb(84, 136, 140);
    public static readonly Color Thicket = Color.FromArgb(104, 132, 88);

    public static readonly Font Mono = new("Consolas", 9f);
    public static readonly Font MonoBold = new("Consolas", 9f, FontStyle.Bold);
    public static readonly Font MonoSmall = new("Consolas", 7.5f);
    public static readonly Font MonoTitle = new("Consolas", 10f, FontStyle.Bold);

    public static Color Alpha(Color c, int a) => Color.FromArgb(Math.Clamp(a, 0, 255), c);

    public static Color Category(EventCategory c) => c switch
    {
        EventCategory.BIO => Color.FromArgb(178, 62, 70),
        EventCategory.BEH => Ink,
        EventCategory.COG => Belief,
        EventCategory.ENV => Color.FromArgb(66, 120, 78),
        _ => Ink,
    };

    public static Color Kind(ObjectKind k) => k switch
    {
        ObjectKind.BerryBush => Color.FromArgb(176, 64, 72),
        ObjectKind.Pool => Color.FromArgb(38, 112, 176),
        ObjectKind.Vent => Color.FromArgb(214, 128, 26),
        ObjectKind.Hollow => Color.FromArgb(88, 104, 72),
        ObjectKind.Nightshade => Color.FromArgb(124, 64, 150),
        _ => Color.FromArgb(128, 132, 130),
    };
}

/// <summary>Map symbols for object kinds — plain geometric marks, survey-map style.</summary>
internal static class Glyphs
{
    public static void Draw(Graphics g, ObjectKind k, PointF p, float r, int alpha = 255)
    {
        var col = Theme.Alpha(Theme.Kind(k), alpha);
        using var brush = new SolidBrush(col);
        using var pen = new Pen(col, Math.Max(1.2f, r * 0.28f));
        switch (k)
        {
            case ObjectKind.BerryBush:
                g.FillPolygon(brush, new[]
                {
                    new PointF(p.X, p.Y - r), new PointF(p.X + r, p.Y),
                    new PointF(p.X, p.Y + r), new PointF(p.X - r, p.Y),
                });
                break;
            case ObjectKind.Pool:
                using (var fill = new SolidBrush(Theme.Alpha(col, alpha * 70 / 255)))
                    g.FillEllipse(fill, p.X - r, p.Y - r, 2 * r, 2 * r);
                g.DrawEllipse(pen, p.X - r, p.Y - r, 2 * r, 2 * r);
                break;
            case ObjectKind.Vent:
                g.FillPolygon(brush, new[]
                {
                    new PointF(p.X, p.Y - r), new PointF(p.X + r, p.Y + r * 0.8f), new PointF(p.X - r, p.Y + r * 0.8f),
                });
                break;
            case ObjectKind.Hollow:
                g.DrawArc(pen, p.X - r, p.Y - r * 0.7f, 2 * r, 2 * r * 0.7f * 2, 180, 180);
                g.DrawLine(pen, p.X - r, p.Y + r * 0.7f, p.X + r, p.Y + r * 0.7f);
                break;
            case ObjectKind.Stone:
            {
                float s = r * 0.55f;
                g.FillRectangle(brush, p.X - s, p.Y - s, 2 * s, 2 * s);
                break;
            }
            case ObjectKind.Fern:
            {
                float s = r * 0.8f;
                using var thin = new Pen(col, 1.2f);
                for (int i = 0; i < 3; i++)
                {
                    double a = i * Math.PI / 3;
                    float dx = (float)Math.Cos(a) * s, dy = (float)Math.Sin(a) * s;
                    g.DrawLine(thin, p.X - dx, p.Y - dy, p.X + dx, p.Y + dy);
                }
                break;
            }
            case ObjectKind.Nightshade:
            {
                float s = r * 0.5f;
                g.FillEllipse(brush, p.X - s, p.Y - s, 2 * s, 2 * s);
                using var thin = new Pen(col, 1f);
                g.DrawEllipse(thin, p.X - r * 0.9f, p.Y - r * 0.9f, r * 1.8f, r * 1.8f);
                break;
            }
        }
    }
}
