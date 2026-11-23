using System.Drawing;
using Core.SimRacing.Race;
using Units = Core.Services.CapsUnits;

namespace Core.SimRacing.Hud.Widgets;

internal sealed class AlertsWidget : HudWidget
{
    public const string Headline = "headline", Stripes = "stripes", Log = "log";
    private const double EntranceSeconds = 0.38;

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("style", "Style", Headline,
            new HudChoice(Headline, "Headline", "The most urgent alert large, the rest as chips"),
            new HudChoice(Stripes, "Stripe stack", "One row per alert"),
            new HudChoice(Log, "Race log", "Lap and age; cleared alerts stay dimmed")),
        HudOption.Int("max", "Alerts at once", 3, 1, 8),
    };

    private float _fade = 1;
    private readonly List<HudAlert> _rows = new();

    public override bool HasContent(HudView v) => v.Race.Alerts.Visible(v.Now).Count > 0;

    public override void Draw(HudPainter p, HudView v)
    {
        var list = v.Race.Alerts.Visible(v.Now);
        int max = v.Options.Int("max");
        string style = v.Options.Choice("style");
        float pad = 6 * p.Density;
        float natural = style switch
        {
            Stripes => StripeRowHeight(p) * max,
            Log => LogRowHeight(p, true) * (max + 1) + 4 * p.Density,
            _ => HeroHeight(p, true) + (max > 1 ? ChipRowHeight(p) : 0),
        };
        p.Need(p.Bounds.Width, natural + pad * 2);
        if (list.Count == 0 && !p.IsPreview) return;
        var area = p.Bounds.Inflated(-pad, -pad);
        _fade = 1;
        float used = style switch
        {
            Stripes => list.Count == 0 ? StripesQuiet(p, v, area, true) : DrawStripes(p, v, area, list, max, true),
            Log => list.Count == 0 ? LogQuiet(p, v, area, true) : DrawLog(p, v, area, list, max, true),
            _ => list.Count == 0 ? HeadlineQuiet(p, v, area, true) : DrawHeadline(p, v, area, list, max, true),
        };
        p.Panel(new RectangleF(0, 0, p.Bounds.Width, Math.Min(p.Bounds.Height, used + pad * 2)));
        _fade = 1;
        switch (style)
        {
            case Stripes:
                if (list.Count == 0) StripesQuiet(p, v, area, false); else DrawStripes(p, v, area, list, max, false);
                break;
            case Log:
                if (list.Count == 0) LogQuiet(p, v, area, false); else DrawLog(p, v, area, list, max, false);
                break;
            default:
                if (list.Count == 0) HeadlineQuiet(p, v, area, false); else DrawHeadline(p, v, area, list, max, false);
                break;
        }
        _fade = 1;
    }

    private static (Color Edge, Color Text) Level(HudPainter p, AlertLevel level) => level switch
    {
        AlertLevel.Critical => (p.P.Danger, p.P.DangerText),
        AlertLevel.Warning => (p.P.Warn, p.P.WarnText),
        AlertLevel.Good => (p.P.Good, p.P.GoodText),
        _ => (p.P.Accent, p.P.Text),
    };

    private Color F(Color c) => _fade >= 0.999f ? c : HudPalette.Alpha(c, (int)Math.Round(c.A * _fade));

    private static float Entrance(HudPainter p, HudView v, HudAlert a)
    {
        if (p.ReducedMotion) return 1;
        double x = Math.Clamp((v.Now - a.RaisedAt) / EntranceSeconds, 0, 1);
        return (float)(1 - Math.Pow(1 - x, 3));
    }

    private static double? Remaining(HudView v, HudAlert a)
    {
        if (a.Sticky) return null;
        double duration = v.Race.Alerts.ChoiceFor(a.Id).Duration;
        return duration > 0 ? Math.Clamp(1 - (v.Now - a.RaisedAt) / duration, 0, 1) : null;
    }

    private static float Wave(HudPainter p, HudView v, double period, double delay = 0) =>
        p.ReducedMotion ? 1 : (float)(0.5 - 0.5 * Math.Cos((v.Now - delay) * Math.PI * 2 / period));

    private static float Pulse(HudPainter p, HudView v) => p.ReducedMotion ? 1 : 0.45f + 0.55f * (1 - Wave(p, v, 1.4));

    private static float Round(HudPainter p, float r) => p.Look.Corner > 0 ? r * p.Density : 0;

    private static Color Tint(HudPainter p, Color c, double amount) => p.Solid(HudPalette.Mix(p.PanelColor, c, amount));

    private Font DetailFont(HudPainter p) => p.Font(13f, false);

    private static float CapHeight(HudPainter p, float px) => p.DisplaySize("H", px).Height;

    private float HeroHeight(HudPainter p, bool detail)
    {
        float d = p.Density;
        float text = CapHeight(p, 26f) + (detail ? 7 * d + p.LineHeight(DetailFont(p)) : 0);
        return 10 * d + Math.Max(30 * d, text) + 12 * d;
    }

    private static float ChipRowHeight(HudPainter p) => 30 * p.Density;

    private static readonly string[] HeadlineParts = { "top", "others" };

    private float DrawHeadline(HudPainter p, HudView v, RectangleF area, IReadOnlyList<HudAlert> list, int max, bool measure)
    {
        float d = p.Density;
        var top = list[0];
        bool detail = top.Detail is { Length: > 0 } && area.Height >= HeroHeight(p, true);
        float heroH = Math.Min(area.Height, HeroHeight(p, detail));
        bool chips = list.Count > 1 && max > 1 && area.Height >= heroH + ChipRowHeight(p);
        float used = heroH + (chips ? ChipRowHeight(p) : 0);
        if (measure) return used;
        var hero = new RectangleF(area.X, area.Y, area.Width, heroH);
        float chipTop = hero.Bottom;
        if (chips)
        {
            Span<RectangleF> boxes = stackalloc RectangleF[2];
            p.Arrange(v, "alerts.headline", new RectangleF(area.X, area.Y, area.Width, used), false, 0, HeadlineParts, stackalloc float[] { heroH, ChipRowHeight(p) }, boxes);
            hero = boxes[0];
            chipTop = boxes[1].Y;
        }
        var (edge, text) = Level(p, top.Level);
        _fade = Entrance(p, v, top);
        p.Fill(hero, F(Tint(p, edge, p.P.Night ? 0.1 : 0.26)), Round(p, 6));
        if (Remaining(v, top) is { } left)
        {
            var clip = p.ClipEnds(hero, Round(p, 6), false, true);
            p.Fill(new RectangleF(hero.X, hero.Bottom - 3 * d, hero.Width * (float)left, 3 * d), F(edge));
            p.Unclip(clip);
        }
        if (p.P.Night) p.Outline(new RectangleF(hero.X + 0.5f, hero.Y + 0.5f, hero.Width - 1, hero.Height - 1), F(HudPalette.Alpha(edge, 200)), 1, Round(p, 6));
        float icon = 30 * d;
        AlertIcons.Level(p, top.Level, new RectangleF(hero.X + 12 * d, hero.Y + (hero.Height - icon) / 2, icon, icon), F(edge), 2.2f);
        float x = hero.X + 12 * d + icon + 12 * d, w = Math.Max(0, hero.Right - 12 * d - x);
        float cap = CapHeight(p, 26f), detailH = detail ? 7 * d + p.LineHeight(DetailFont(p)) : 0;
        float y = hero.Y + (hero.Height - cap - detailH) / 2;
        float px = 26f;
        while (px > 18f && p.DisplaySize(top.Title, px).Width > w) px -= 2f;
        if (p.DisplaySize(top.Title, px).Width <= w) p.Display(top.Title, px, F(p.P.Heading), new RectangleF(x, y, w, cap), HAlign.Left, VAlign.Center);
        else p.Text(top.Title, p.Big, F(p.P.Heading), new RectangleF(x, y - 4 * d, w, cap + 8 * d), HAlign.Left, VAlign.Center);
        if (detail) p.Text(top.Detail!, DetailFont(p), F(text), new RectangleF(x, y + cap + 4 * d, w, p.LineHeight(DetailFont(p))), HAlign.Left, VAlign.Top);
        if (!chips) return used;
        var chipFont = p.Font(12f, true);
        float cx = area.X, cy = chipTop + 6 * d, ch = 24 * d;
        for (int i = 1; i < list.Count && i < max; i++)
        {
            var a = list[i];
            float cw = 8 * d + 7 * d + 6 * d + p.Measure(a.Title, chipFont) + 10 * d;
            if (cx + cw > area.Right + 0.5f) break;
            var (c, _) = Level(p, a.Level);
            _fade = Entrance(p, v, a);
            p.Fill(new RectangleF(cx, cy, cw, ch), F(HudPalette.Alpha(c, 41)), Round(p, 12));
            p.Dot(cx + 8 * d + 3.5f * d, cy + ch / 2, 3.5f * d, F(c));
            p.Text(a.Title, chipFont, F(p.P.Text), new RectangleF(cx + 8 * d + 7 * d + 6 * d, cy, cw - 21 * d, ch), HAlign.Left, VAlign.Center, false);
            cx += cw + 6 * d;
        }
        _fade = 1;
        return used;
    }

    private float HeadlineQuiet(HudPainter p, HudView v, RectangleF area, bool measure)
    {
        float d = p.Density;
        float h = Math.Min(area.Height, 62 * d);
        if (measure) return h;
        var r = new RectangleF(area.X, area.Y, area.Width, h);
        AlertIcons.Dashed(p, r, p.P.LineStrong, Round(p, 6));
        var font = p.Font(13f, false);
        const string label = "Alerts show here";
        float dot = 8 * d, gap = 6 * d, dots = dot * 4 + gap * 3, total = dots + 14 * d + p.Measure(label, font);
        float x = r.X + (r.Width - total) / 2, cy = r.Y + h / 2;
        Span<AlertLevel> order = stackalloc[] { AlertLevel.Critical, AlertLevel.Warning, AlertLevel.Info, AlertLevel.Good };
        for (int i = 0; i < 4; i++)
        {
            float k = Wave(p, v, 2.7, i * 0.45);
            var (c, _) = Level(p, order[i]);
            p.Dot(x + i * (dot + gap) + dot / 2, cy, dot / 2 * (0.85f + 0.15f * k), HudPalette.Alpha(c, (int)(255 * (0.35f + 0.65f * k))));
        }
        p.Text(label, font, p.P.Faint, new RectangleF(x + dots + 14 * d, r.Y, r.Right - x - dots - 14 * d, h), HAlign.Left, VAlign.Center);
        return h;
    }

    private static float StripeRowHeight(HudPainter p) =>
        Math.Max(30 * p.Density, p.LineHeight(p.Font(16f, true)) + p.LineHeight(p.Font(12f, false))) + 17 * p.Density;

    private float DrawStripes(HudPainter p, HudView v, RectangleF area, IReadOnlyList<HudAlert> list, int max, bool measure)
    {
        float d = p.Density, rowH = StripeRowHeight(p);
        int rows = Math.Max(1, Math.Min(Math.Min(max, list.Count), (int)(area.Height / rowH)));
        if (measure) return Math.Min(area.Height, rows * rowH);
        var titleFont = p.Font(16f, true);
        var detailFont = p.Font(12f, false);
        var sideFont = p.Font(11f, true);
        for (int i = 0; i < rows; i++)
        {
            var a = list[i];
            var r = new RectangleF(area.X, area.Y + i * rowH, area.Width, Math.Min(rowH, area.Height - i * rowH));
            var (edge, _) = Level(p, a.Level);
            _fade = Entrance(p, v, a);
            if (i > 0) p.Line(r.X, r.Y, r.Right, r.Y, p.P.Line);
            float stripeAlpha = a.Sticky ? Pulse(p, v) : 1;
            p.Fill(new RectangleF(r.X, r.Y + 6 * d, 3 * d, r.Height - 12 * d), F(HudPalette.Alpha(edge, (int)(255 * stripeAlpha))), Round(p, 1.5f));
            float badge = 30 * d, bx = r.X + 14 * d, cy = r.Y + (r.Height - 1 * d) / 2;
            p.Dot(bx + badge / 2, cy, badge / 2, F(HudPalette.Alpha(edge, 51)));
            float icon = 18 * d;
            AlertIcons.Level(p, a.Level, new RectangleF(bx + (badge - icon) / 2, cy - icon / 2, icon, icon), F(edge), 2f);
            float x = bx + badge + 10 * d, right = r.Right - 10 * d;
            if (a.Sticky)
            {
                const string held = "Held";
                float hw = p.Measure(held, sideFont);
                p.Text(held, sideFont, F(p.P.Faint), new RectangleF(right - hw, r.Y, hw, r.Height), HAlign.Right, VAlign.Center, false);
                right -= hw + 10 * d;
            }
            bool detail = a.Detail is { Length: > 0 };
            float block = p.LineHeight(titleFont) + (detail ? p.LineHeight(detailFont) : 0), ty = cy - block / 2;
            p.Text(a.Title, titleFont, F(p.P.Heading), new RectangleF(x, ty, Math.Max(0, right - x), p.LineHeight(titleFont)), HAlign.Left, VAlign.Top);
            if (detail) p.Text(a.Detail!, detailFont, F(p.P.Muted), new RectangleF(x, ty + p.LineHeight(titleFont), Math.Max(0, right - x), p.LineHeight(detailFont)), HAlign.Left, VAlign.Top);
            if (Remaining(v, a) is { } left)
                p.Fill(new RectangleF(r.X + 14 * d, r.Bottom - 4 * d, (r.Width - 24 * d) * (float)left, 2 * d), F(HudPalette.Alpha(edge, 179)), Round(p, 1));
        }
        _fade = 1;
        return rows * rowH;
    }

    private static float StripesQuiet(HudPainter p, HudView v, RectangleF area, bool measure)
    {
        float d = p.Density, h = Math.Min(area.Height, 40 * d);
        if (measure) return h;
        float k = Wave(p, v, 2.4), cy = area.Y + h / 2, dot = 8 * d;
        p.Dot(area.X + 12 * d + dot / 2, cy, dot / 2 * (0.85f + 0.15f * k), HudPalette.Alpha(p.P.Good, (int)(255 * (0.35f + 0.65f * k))));
        var label = p.Font(14f, true);
        float x = area.X + 12 * d + dot + 10 * d;
        const string clear = "All clear";
        float lw = p.Measure(clear, label);
        p.Text(clear, label, p.P.GoodText, new RectangleF(x, area.Y, lw + 2, h), HAlign.Left, VAlign.Center, false);
        int watched = 0;
        foreach (var def in AlertCatalog.All) if (v.Race.Alerts.ChoiceFor(def.Id).Enabled) watched++;
        float mx = x + lw + 12 * d;
        p.Text(PooledText.Concat("Watching ", RaceFormat.Count(watched, "alert")), p.Small, p.P.Faint, new RectangleF(mx, area.Y, Math.Max(0, area.Right - 10 * d - mx), h), HAlign.Right, VAlign.Center);
        return h;
    }

    private static float LogRowHeight(HudPainter p, bool detail) =>
        10 * p.Density + p.LineHeight(p.Font(15f, true)) + (detail ? p.LineHeight(p.Font(12f, false)) : 0);

    private float DrawLog(HudPainter p, HudView v, RectangleF area, IReadOnlyList<HudAlert> list, int max, bool measure)
    {
        _rows.Clear();
        for (int i = 0; i < list.Count && i < max; i++) _rows.Add(list[i]);
        var history = v.Race.Alerts.History;
        int cleared = 0;
        for (int i = history.Count - 1; i >= 0 && cleared < 2 && _rows.Count < max + 1; i--)
        {
            var h = history[i];
            bool live = false;
            for (int j = 0; j < list.Count; j++) if (ReferenceEquals(list[j], h)) { live = true; break; }
            if (live) continue;
            _rows.Add(h);
            cleared++;
        }
        return LogRows(p, v, area, _rows, list, measure, null);
    }

    private float LogRows(HudPainter p, HudView v, RectangleF area, List<HudAlert> rows, IReadOnlyList<HudAlert> live, bool measure, float? dim)
    {
        float d = p.Density;
        var titleFont = p.Font(15f, true);
        var detailFont = p.Font(12f, false);
        var whenFont = p.Font(11f, true);
        float top = area.Y + 2 * d, y = top;
        int shown = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            float h = LogRowHeight(p, rows[i].Detail is { Length: > 0 });
            if (shown > 0 && y + h > area.Bottom - 2 * d) break;
            y += h;
            shown++;
        }
        float used = y - area.Y + 2 * d;
        if (measure) return Math.Min(area.Height, used);
        float whenW = area.Width >= 250 * d ? 74 * d + 6 * d : 0;
        float nodeX = area.X + whenW + 3 * d + 4.5f * d;
        float titleLine = p.LineHeight(titleFont), nodeR = 4.5f * d, clearance = nodeR + 3 * d;
        y = top;
        for (int i = 0; i + 1 < shown; i++)
        {
            float h = LogRowHeight(p, rows[i].Detail is { Length: > 0 });
            float from = y + 5 * d + titleLine / 2 + clearance, to = y + h + 5 * d + titleLine / 2 - clearance;
            if (to > from) p.Line(nodeX, from, nodeX, to, p.P.Line);
            y += h;
        }
        y = top;
        for (int i = 0; i < shown; i++)
        {
            var a = rows[i];
            bool isLive = false;
            for (int j = 0; j < live.Count; j++) if (ReferenceEquals(live[j], a)) { isLive = true; break; }
            bool detail = a.Detail is { Length: > 0 };
            float h = LogRowHeight(p, detail);
            var (edge, _) = Level(p, a.Level);
            _fade = (dim ?? (isLive ? 1f : 0.42f)) * (isLive ? Entrance(p, v, a) : 1f);
            float titleH = p.LineHeight(titleFont), lineY = y + 5 * d;
            if (whenW > 0) p.Text(When(v, a), whenFont, F(p.P.Faint), new RectangleF(area.X, lineY, 74 * d, titleH), HAlign.Right, VAlign.Center, false);
            float cy = lineY + titleH / 2, nr = 4.5f * d;
            if (isLive) p.Dot(nodeX, cy, nr, F(HudPalette.Alpha(edge, (int)(255 * (a.Sticky ? Pulse(p, v) : 1)))));
            else p.G.DrawEllipse(p.Pen(F(edge), 2 * d), nodeX - nr + d, cy - nr + d, nr * 2 - 2 * d, nr * 2 - 2 * d);
            float x = area.X + whenW + 16 * d + 6 * d, w = Math.Max(0, area.Right - 4 * d - x);
            p.Text(a.Title, titleFont, F(isLive ? p.P.Heading : p.P.Muted), new RectangleF(x, lineY, w, titleH), HAlign.Left, VAlign.Top);
            if (detail) p.Text(a.Detail!, detailFont, F(p.P.Muted), new RectangleF(x, lineY + titleH, w, p.LineHeight(detailFont)), HAlign.Left, VAlign.Top);
            y += h;
        }
        _fade = 1;
        return used;
    }

    private static string When(HudView v, HudAlert a)
    {
        double age = Math.Max(0, v.Now - a.RaisedAt);
        if (a.Lap is not { } lap) return age < 5 ? "NOW" : age < 60 ? PooledText.Number("", (long)age, Seconds) : PooledText.Number("", (long)(age / 60), Minutes);
        return age < 5 ? PooledText.Number("L", lap, " · NOW") : age < 60 ? PooledText.Number("L", lap, " · ", (long)age, Seconds) : PooledText.Number("L", lap, " · ", (long)(age / 60), Minutes);
    }

    private const string Seconds = " " + Units.S, Minutes = " MIN";

    private float LogQuiet(HudPainter p, HudView v, RectangleF area, bool measure)
    {
        _rows.Clear();
        var history = v.Race.Alerts.History;
        for (int i = history.Count - 1; i >= 0 && _rows.Count < 2; i--) _rows.Add(history[i]);
        if (_rows.Count == 0)
        {
            _rows.Add(new("pit.window", "pit.window", "Pit window open", "One stop reaches the flag", AlertLevel.Info, v.Now - 120, false, 22));
            _rows.Add(new("lap.pb", "lap.pb", "Personal best", "1:42.318 · −0.214", AlertLevel.Good, v.Now - 360, false, 19));
        }
        return LogRows(p, v, area, _rows, Array.Empty<HudAlert>(), measure, 0.5f);
    }
}

internal static class AlertIcons
{
    private static readonly PointF[] Octagon = { new(8.5f, 3), new(15.5f, 3), new(21, 8.5f), new(21, 15.5f), new(15.5f, 21), new(8.5f, 21), new(3, 15.5f), new(3, 8.5f) };
    private static readonly PointF[] Triangle = { new(12, 4), new(21, 20), new(3, 20) };
    private static readonly PointF[] Tick = { new(8, 12.5f), new(10.7f, 15.2f), new(16, 9.8f) };

    public static void Level(HudPainter p, AlertLevel level, RectangleF box, Color c, float stroke)
    {
        var g = p.G;
        var state = g.Save();
        g.TranslateTransform(box.X, box.Y);
        g.ScaleTransform(box.Width / 24f, box.Height / 24f);
        var pen = p.Pen(c, stroke);
        switch (level)
        {
            case AlertLevel.Critical:
                g.DrawPolygon(pen, Octagon);
                g.DrawLine(pen, 9.5f, 9.5f, 14.5f, 14.5f);
                g.DrawLine(pen, 14.5f, 9.5f, 9.5f, 14.5f);
                break;
            case AlertLevel.Warning:
                g.DrawPolygon(pen, Triangle);
                g.DrawLine(pen, 12, 10, 12, 14);
                g.DrawLine(pen, 12, 17, 12, 17.4f);
                break;
            case AlertLevel.Good:
                g.DrawEllipse(pen, 3.5f, 3.5f, 17, 17);
                g.DrawLines(pen, Tick);
                break;
            default:
                g.DrawEllipse(pen, 3.5f, 3.5f, 17, 17);
                g.DrawLine(pen, 12, 11, 12, 16);
                g.DrawLine(pen, 12, 8, 12, 8.4f);
                break;
        }
        g.Restore(state);
    }

    public static void Dashed(HudPainter p, RectangleF r, Color c, float radius)
    {
        var g = p.G;
        var pen = p.Pen(c, 1);
        float x0 = r.X + 0.5f, y0 = r.Y + 0.5f, x1 = r.Right - 0.5f, y1 = r.Bottom - 0.5f, rad = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
        if (rad > 0.5f)
        {
            float dd = rad * 2;
            g.DrawArc(pen, x0, y0, dd, dd, 180, 90);
            g.DrawArc(pen, x1 - dd, y0, dd, dd, 270, 90);
            g.DrawArc(pen, x1 - dd, y1 - dd, dd, dd, 0, 90);
            g.DrawArc(pen, x0, y1 - dd, dd, dd, 90, 90);
        }
        float dash = 3 * p.Density, gap = 3 * p.Density;
        for (float x = x0 + rad; x < x1 - rad; x += dash + gap)
        {
            float e = Math.Min(x + dash, x1 - rad);
            g.DrawLine(pen, x, y0, e, y0);
            g.DrawLine(pen, x, y1, e, y1);
        }
        for (float y = y0 + rad; y < y1 - rad; y += dash + gap)
        {
            float e = Math.Min(y + dash, y1 - rad);
            g.DrawLine(pen, x0, y, x0, e);
            g.DrawLine(pen, x1, y, x1, e);
        }
    }
}
