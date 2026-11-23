using System.Drawing;
using Core.Config;

namespace Core.SimRacing.Hud;

internal sealed record HudMonitor(string Id, RectangleF Bounds, float Scale, bool Primary);

internal readonly record struct HudGuide(PointF From, PointF To, bool Gap);

internal static class HudGeometry
{
    private static (float Fx, float Fy) Fraction(HudAnchor a) => a switch
    {
        HudAnchor.TopLeft => (0, 0), HudAnchor.Top => (0.5f, 0), HudAnchor.TopRight => (1, 0),
        HudAnchor.Left => (0, 0.5f), HudAnchor.Center => (0.5f, 0.5f), HudAnchor.Right => (1, 0.5f),
        HudAnchor.BottomLeft => (0, 1), HudAnchor.Bottom => (0.5f, 1), _ => (1, 1),
    };

    public static HudMonitor MonitorFor(string? id, IReadOnlyList<HudMonitor> monitors)
    {
        if (monitors.Count == 0) return new HudMonitor("primary", new RectangleF(0, 0, 1920, 1080), 1, true);
        if (!string.Equals(id, "primary", StringComparison.OrdinalIgnoreCase))
            foreach (var m in monitors) if (string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)) return m;
        return monitors.FirstOrDefault(m => m.Primary) ?? monitors[0];
    }

    public static HudMonitor MonitorAt(PointF p, IReadOnlyList<HudMonitor> monitors)
    {
        foreach (var m in monitors) if (m.Bounds.Contains(p)) return m;
        return monitors.OrderBy(m => Distance(m.Bounds, p)).FirstOrDefault() ?? MonitorFor(null, monitors);
    }

    private static double Distance(RectangleF r, PointF p)
    {
        double dx = Math.Max(Math.Max(r.Left - p.X, 0), p.X - r.Right), dy = Math.Max(Math.Max(r.Top - p.Y, 0), p.Y - r.Bottom);
        return dx * dx + dy * dy;
    }

    public static RectangleF Place(HudWidgetConfig w, HudMonitor m) => Place(w, m, new SizeF((float)w.W, (float)w.H));

    public static RectangleF Place(HudWidgetConfig w, HudMonitor m, SizeF size)
    {
        float s = m.Scale, k = s * (float)w.Scale, width = size.Width * k, height = size.Height * k;
        var (fx, fy) = Fraction(w.Anchor);
        float ax = m.Bounds.Left + m.Bounds.Width * fx, ay = m.Bounds.Top + m.Bounds.Height * fy;
        var r = new RectangleF(ax + (float)w.X * s - width * fx, ay + (float)w.Y * s - height * fy, width, height);
        return Clamp(r, m.Bounds);
    }

    public static void Store(HudWidgetConfig w, RectangleF r, HudMonitor m, SizeF size)
    {
        float s = m.Scale <= 0 ? 1 : m.Scale;
        if (size.Width > 0) w.Scale = Math.Round(Math.Clamp(r.Width / (size.Width * s), HudWidgetConfig.MinScale, HudWidgetConfig.MaxScale), 3);
        Anchor(w, r, m);
        w.Clamp();
    }

    public static float ScaleFor(RectangleF r, SizeF size, HudMonitor m, float grid, bool snap)
    {
        float s = m.Scale <= 0 ? 1 : m.Scale;
        float scale = r.Width / Math.Max(1, size.Width * s);
        if (snap && grid > 1)
        {
            float byRight = SnapEdge(r.Left, size.Width * s, scale, m.Bounds.Left, grid);
            float byBottom = SnapEdge(r.Top, size.Height * s, scale, m.Bounds.Top, grid);
            scale = Math.Abs(byRight - scale) * size.Width <= Math.Abs(byBottom - scale) * size.Height ? byRight : byBottom;
        }
        return (float)Math.Clamp(scale, HudWidgetConfig.MinScale, HudWidgetConfig.MaxScale);
    }

    private static float SnapEdge(float start, float unit, float scale, float origin, float grid)
    {
        float edge = start + unit * scale;
        float snapped = origin + MathF.Round((edge - origin) / grid) * grid;
        if (snapped <= start) snapped += grid;
        return (snapped - start) / Math.Max(1, unit);
    }

    public static void Store(HudWidgetConfig w, RectangleF r, HudMonitor m)
    {
        float s = (m.Scale <= 0 ? 1 : m.Scale) * (float)(w.Scale > 0 ? w.Scale : 1);
        w.W = Math.Round(r.Width / s, 1);
        w.H = Math.Round(r.Height / s, 1);
        Anchor(w, r, m);
        w.Clamp();
    }

    private static void Anchor(HudWidgetConfig w, RectangleF r, HudMonitor m)
    {
        float s = m.Scale <= 0 ? 1 : m.Scale;
        float cx = (r.Left + r.Width / 2 - m.Bounds.Left) / m.Bounds.Width, cy = (r.Top + r.Height / 2 - m.Bounds.Top) / m.Bounds.Height;
        int col = cx < 1 / 3f ? 0 : cx < 2 / 3f ? 1 : 2, row = cy < 1 / 3f ? 0 : cy < 2 / 3f ? 1 : 2;
        w.Anchor = (HudAnchor)(row * 3 + col);
        var (fx, fy) = Fraction(w.Anchor);
        float ax = m.Bounds.Left + m.Bounds.Width * fx, ay = m.Bounds.Top + m.Bounds.Height * fy;
        w.Monitor = m.Primary ? "primary" : m.Id;
        w.X = Math.Round((r.Left + r.Width * fx - ax) / s, 1);
        w.Y = Math.Round((r.Top + r.Height * fy - ay) / s, 1);
    }

    public static RectangleF Clamp(RectangleF r, RectangleF area)
    {
        float w = Math.Min(r.Width, area.Width), h = Math.Min(r.Height, area.Height);
        float x = Math.Clamp(r.X, area.Left, area.Right - w), y = Math.Clamp(r.Y, area.Top, area.Bottom - h);
        return new RectangleF(x, y, w, h);
    }

    public static RectangleF Snap(RectangleF r, IEnumerable<RectangleF> others, RectangleF monitor, float grid, float threshold, bool resizing)
    {
        if (!resizing) return Align(r, others.ToList(), monitor, grid, threshold, null);
        var xs = new List<float> { monitor.Left, monitor.Right };
        var ys = new List<float> { monitor.Top, monitor.Bottom };
        foreach (var o in others)
        {
            xs.Add(o.Left); xs.Add(o.Right);
            ys.Add(o.Top); ys.Add(o.Bottom);
        }
        float SnapTo(float v, List<float> lines, float origin)
        {
            float best = v, bestD = threshold + 0.001f;
            foreach (var l in lines)
                if (Math.Abs(l - v) < bestD) { best = l; bestD = Math.Abs(l - v); }
            if (bestD <= threshold) return best;
            return grid > 1 ? origin + MathF.Round((v - origin) / grid) * grid : v;
        }
        float right = SnapTo(r.Right, xs, monitor.Left), bottom = SnapTo(r.Bottom, ys, monitor.Top);
        return RectangleF.FromLTRB(r.Left, r.Top, Math.Max(r.Left + 20, right), Math.Max(r.Top + 20, bottom));
    }

    public static RectangleF Align(RectangleF r, IReadOnlyList<RectangleF> others, RectangleF monitor, float grid, float threshold, List<HudGuide>? guides, bool alongX = true, bool alongY = true)
    {
        var marks = guides is null ? null : new List<Mark>();
        var xSpans = others.Select(o => new Span(o.Left, o.Right, o.Top, o.Bottom)).ToList();
        float x = alongX ? AlignAxis(new Span(r.Left, r.Right, r.Top, r.Bottom), monitor.Left, monitor.Right, monitor.Top, monitor.Bottom, xSpans, grid, threshold, marks) : r.Left;
        if (marks is not null) foreach (var m in marks) guides!.Add(m.Line ? new HudGuide(new PointF(m.At, m.From), new PointF(m.At, m.To), false) : new HudGuide(new PointF(m.From, m.At), new PointF(m.To, m.At), true));
        marks?.Clear();
        var ySpans = others.Select(o => new Span(o.Top, o.Bottom, o.Left, o.Right)).ToList();
        float y = alongY ? AlignAxis(new Span(r.Top, r.Bottom, x, x + r.Width), monitor.Top, monitor.Bottom, monitor.Left, monitor.Right, ySpans, grid, threshold, marks) : r.Top;
        if (marks is not null) foreach (var m in marks) guides!.Add(m.Line ? new HudGuide(new PointF(m.From, m.At), new PointF(m.To, m.At), false) : new HudGuide(new PointF(m.At, m.From), new PointF(m.At, m.To), true));
        return new RectangleF(x, y, r.Width, r.Height);
    }

    private readonly record struct Span(float Lo, float Hi, float CLo, float CHi)
    {
        public float Mid => (Lo + Hi) / 2;
        public bool Faces(Span o) => Math.Min(CHi, o.CHi) - Math.Max(CLo, o.CLo) > 0.5f;
        public float SharedMid(Span o) => (Math.Max(CLo, o.CLo) + Math.Min(CHi, o.CHi)) / 2;
    }

    private readonly record struct Mark(bool Line, float At, float From, float To);

    private static float AlignAxis(Span r, float monLo, float monHi, float monCLo, float monCHi, IReadOnlyList<Span> others, float grid, float threshold, List<Mark>? marks)
    {
        float size = r.Hi - r.Lo;
        var found = new List<(float Start, Mark[] Marks)>
        {
            (monLo, Array.Empty<Mark>()),
            (monHi - size, Array.Empty<Mark>()),
            ((monLo + monHi - size) / 2, new[] { new Mark(true, (monLo + monHi) / 2, monCLo, monCHi) }),
        };
        foreach (var o in others)
        {
            float from = Math.Min(o.CLo, r.CLo), to = Math.Max(o.CHi, r.CHi);
            found.Add((o.Lo, new[] { new Mark(true, o.Lo, from, to) }));
            found.Add((o.Hi, new[] { new Mark(true, o.Hi, from, to) }));
            found.Add((o.Lo - size, new[] { new Mark(true, o.Lo, from, to) }));
            found.Add((o.Hi - size, new[] { new Mark(true, o.Hi, from, to) }));
            found.Add((o.Mid - size / 2, new[] { new Mark(true, o.Mid, from, to) }));
        }
        float maxGap = (monHi - monLo) / 3;
        foreach (var a in others)
            foreach (var b in others)
            {
                float gap = b.Lo - a.Hi;
                if (gap < 1 || gap > maxGap || !a.Faces(b)) continue;
                if (r.Faces(a) && r.Faces(b))
                {
                    if (gap > size + 2)
                    {
                        float mid = (a.Hi + b.Lo - size) / 2;
                        found.Add((mid, new[] { new Mark(false, r.SharedMid(a), a.Hi, mid), new Mark(false, r.SharedMid(b), mid + size, b.Lo) }));
                    }
                    if (r.Hi > a.Hi && r.Lo < b.Lo) continue;
                }
                var reference = new Mark(false, a.SharedMid(b), a.Hi, b.Lo);
                foreach (var o in others)
                {
                    if (!r.Faces(o)) continue;
                    float level = r.SharedMid(o);
                    if (o != a) found.Add((o.Hi + gap, new[] { reference, new Mark(false, level, o.Hi, o.Hi + gap) }));
                    if (o != b) found.Add((o.Lo - gap - size, new[] { reference, new Mark(false, level, o.Lo - gap, o.Lo) }));
                }
            }
        float best = r.Lo, bestD = threshold + 0.001f;
        foreach (var (start, _) in found)
        {
            float d = Math.Abs(start - r.Lo);
            if (d < bestD) { best = start; bestD = d; }
        }
        if (bestD > threshold) return grid > 1 ? monLo + MathF.Round((r.Lo - monLo) / grid) * grid : r.Lo;
        if (marks is not null)
            foreach (var (start, list) in found)
                if (Math.Abs(start - best) < 0.5f)
                    foreach (var m in list)
                        if (!marks.Contains(m)) marks.Add(m);
        return best;
    }

    public static void SnapAll(IEnumerable<HudWidgetConfig> widgets, IReadOnlyList<HudMonitor> monitors, float grid, Func<HudWidgetConfig, SizeF>? sizeOf = null)
    {
        var placed = new List<(string Monitor, RectangleF Rect)>();
        foreach (var w in widgets)
        {
            var m = MonitorFor(w.Monitor, monitors);
            var others = placed.Where(p => p.Monitor == m.Id).Select(p => p.Rect);
            var size = sizeOf?.Invoke(w) ?? new SizeF((float)w.W, (float)w.H);
            var r = Clamp(Snap(Place(w, m, size), others, m.Bounds, grid * m.Scale, 10 * m.Scale, resizing: false), m.Bounds);
            Store(w, r, m, size);
            placed.Add((m.Id, r));
        }
    }

    public static RectangleF Resolve(RectangleF r, IReadOnlyList<RectangleF> others, RectangleF monitor)
    {
        var current = r;
        for (int pass = 0; pass < 8; pass++)
        {
            var hit = others.FirstOrDefault(o => o.IntersectsWith(current) && Overlap(o, current) > 1);
            if (hit.IsEmpty) return current;
            var options = new[]
            {
                new RectangleF(hit.Left - current.Width, current.Y, current.Width, current.Height),
                new RectangleF(hit.Right, current.Y, current.Width, current.Height),
                new RectangleF(current.X, hit.Top - current.Height, current.Width, current.Height),
                new RectangleF(current.X, hit.Bottom, current.Width, current.Height),
            }
            .Where(o => monitor.Contains(o))
            .OrderBy(o => Math.Abs(o.X - current.X) + Math.Abs(o.Y - current.Y))
            .ToList();
            var free = options.FirstOrDefault(o => !others.Any(x => x.IntersectsWith(o) && Overlap(x, o) > 1));
            if (!free.IsEmpty) return free;
            if (options.Count == 0) return r;
            current = options[0];
        }
        return r;
    }

    private static float Overlap(RectangleF a, RectangleF b)
    {
        var i = RectangleF.Intersect(a, b);
        return i.IsEmpty ? 0 : i.Width * i.Height;
    }
}
