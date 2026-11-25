using System.Drawing;
using Core.SimRacing.Race;
using Units = Core.Services.CapsUnits;

namespace Core.SimRacing.Hud.Widgets;

internal sealed class ControlsWidget : HudWidget
{
    public const string Big = "big", Strip = "strip", Knob = "knob";
    public const double W = 380, H = 105;
    private const double SampleAge = 1;
    private const int KnobTicks = 11, KnobMaxLit = 5;

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("style", "Style", Strip,
            new HudChoice(Big, "Big number", "The changed control alone, in big digits"),
            new HudChoice(Strip, "Control strip", "Every in-car control in one row, the changed one lit"),
            new HudChoice(Knob, "Knob", "Drawn like a rotary knob with the old value")),
        TitleRow,
        HudOption.Number("hold", "Show for", 3, 1, 10, 0.5, Units.S, "How long it stays after a change"),
        HudOption.Bool("stay", "Stay on screen", false, "Keep the strip shown whenever the car has controls"),
    };

    private ControlLog? _sample;
    private int? _sampleLap;
    private readonly List<ControlItem> _shown = new();

    public override bool HasContent(HudView v)
    {
        var log = v.Race.Controls;
        if (log.ShownAfterChange(v.Now, v.Options.Number("hold"))) return true;
        return v.Options.Choice("style") == Strip && v.Options.Bool("stay") && log.PresentCount > 0;
    }

    private (ControlLog Log, ControlItem? Changed, double Age, bool Live) Pick(HudPainter p, HudView v)
    {
        var log = v.Race.Controls;
        if (log.ShownAfterChange(v.Now, v.Options.Number("hold"))) return (log, log.LastChanged, v.Now - log.LastChangeAt, true);
        bool sample = p.IsPreview || v.Frame.Simulated;
        if (!sample) return (log, null, double.PositiveInfinity, true);
        var s = Sample(v);
        return (s, s.LastChanged, SampleAge, false);
    }

    private ControlLog Sample(HudView v)
    {
        var f = v.Frame;
        int lap = f.Lap is > 6 and var l ? l : 14;
        if (_sample is not null && _sampleLap == lap) return _sample;
        var s = new ControlLog();
        double bias = f.BrakeBias ?? 54.5, tc = f.TractionControl ?? 4, abs = f.Abs ?? 6, map = f.EngineMap ?? 1;
        s.Set(ControlLog.BrakeBiasKey, bias - 0.5, false, 0, lap - 5);
        s.Set(ControlLog.TractionKey, tc + 1, false, 0, lap - 5);
        s.Set(ControlLog.AbsKey, abs + 1, false, 0, lap - 5);
        s.Set(ControlLog.MapKey, map, false, 0, lap - 5);
        s.Set(ControlLog.AbsKey, abs, true, 1, lap - 5);
        s.Set(ControlLog.TractionKey, tc, true, 2, lap - 2);
        s.Set(ControlLog.BrakeBiasKey, bias, true, 4, lap);
        _sample = s;
        _sampleLap = lap;
        return s;
    }

    public override void Draw(HudPainter p, HudView v)
    {
        string style = v.Options.Choice("style");
        if (style == Strip)
        {
            if (DrawWaiting(p, v)) return;
            DrawStrip(p, v);
            return;
        }
        if (DrawWaiting(p, v, "Car controls")) return;
        var (log, changed, age, _) = Pick(p, v);
        if (changed is null)
        {
            var area = p.Header("Car controls");
            p.Text("Shows when you change a control", p.Small, p.P.Faint, area, HAlign.Center, VAlign.Center);
            return;
        }
        if (p.Bounds.Height < 96 * p.Density) Compact(p, changed);
        else if (style == Knob) DrawKnob(p, v, log, changed, age);
        else DrawBig(p, v, log, changed, age);
    }

    private static string Arrow(ControlItem item) => item.Step is { } s ? s > 0 ? "▲" : "▼" : "";

    private static string StepText(ControlItem item) => item.Step is { } s ? PooledText.Concat(Arrow(item), " ", ControlLog.FormatStep(s)) : "";

    private static Color Fresh(HudPainter p, double age) =>
        p.ReducedMotion || age >= 0.8 ? p.P.Heading : HudPalette.Mix(p.P.AccentText, p.P.Heading, Math.Clamp(age / 0.8, 0, 1));

    private static readonly string[] StripParts = { "controls", "summary" };

    private void DrawStrip(HudPainter p, HudView v)
    {
        float d = p.Density;
        var (log, changed, age, live) = Pick(p, v);
        if (changed is null && !(v.Options.Bool("stay") && log.PresentCount > 0))
        {
            p.Panel();
            p.Text("Shows when you change a control", p.Small, p.P.Faint, p.Bounds.Inflated(-10 * d, -6 * d), HAlign.Center, VAlign.Center);
            return;
        }
        var labelFont = p.Label;
        var valueFont = p.Font(17f, true);
        var subFont = p.Tiny;
        float tileH = 6 * d + p.LineHeight(labelFont) + p.LineHeight(valueFont) + p.LineHeight(subFont) + 4 * d;
        float footH = p.LineHeight(p.Small) + 6 * d;
        var area = BodyPanel(p);
        p.NeedBelow(area, tileH + 4 * d + footH);
        _shown.Clear();
        foreach (var item in log.Items) if (item.Present) _shown.Add(item);
        if (_shown.Count == 0) return;
        float gap = 4 * d, minW = 58 * d, maxW = 140 * d;
        int fit = Math.Max(1, (int)((area.Width + gap) / (minW + gap)));
        int n = Math.Min(fit, _shown.Count);
        if (changed is not null)
        {
            int at = _shown.IndexOf(changed);
            if (at >= n) { _shown.RemoveAt(at); _shown.Insert(n - 1, changed); }
        }
        float tw = Math.Min(maxW, (area.Width - gap * (n - 1)) / n);
        p.NeedWidth(area.X + Math.Min(_shown.Count, 6) * (minW + gap) + 4 * d);
        var order = v.Options.Arranged("controls.strip", StripParts);
        bool footFirst = order[0] == 1;
        bool footShown = footFirst ? area.Y + footH <= area.Bottom + 1 : area.Y + tileH + 4 * d + footH <= area.Bottom + 1;
        Span<RectangleF> parts = stackalloc RectangleF[2];
        p.Arrange(v, "controls.strip", area, false, 4 * d, StripParts, stackalloc float[] { tileH, footShown ? footH : -1 }, parts);
        float x = area.X;
        for (int i = 0; i < n; i++)
        {
            var item = _shown[i];
            var r = new RectangleF(x, parts[0].Y, tw, tileH);
            bool lit = ReferenceEquals(item, changed);
            if (lit) p.Fill(r, p.P.Accent, p.CornerRadius(4)); else p.Tile(r);
            var fg = lit ? p.P.OnAccent : p.P.Heading;
            var muted = lit ? HudPalette.Alpha(p.P.OnAccent, 210) : p.P.Muted;
            float y = r.Y + 4 * d;
            p.Text(p.LabelText(item.Label), labelFont, muted, new RectangleF(r.X + 3 * d, y, r.Width - 6 * d, p.LineHeight(labelFont)), HAlign.Center, VAlign.Top);
            y += p.LineHeight(labelFont);
            p.Text(ControlLog.Format(item.Value), valueFont, fg, new RectangleF(r.X + 2 * d, y, r.Width - 4 * d, p.LineHeight(valueFont)), HAlign.Center, VAlign.Top);
            y += p.LineHeight(valueFont);
            string sub = lit ? StepText(item) : Math.Abs(item.Value - item.Start) > 1e-4 ? PooledText.Concat("start ", ControlLog.Format(item.Start)) : "";
            if (sub.Length > 0) p.Text(sub, subFont, lit ? fg : p.P.Faint, new RectangleF(r.X + 2 * d, y, r.Width - 4 * d, p.LineHeight(subFont)), HAlign.Center, VAlign.Top);
            x += tw + gap;
        }
        if (!footShown) return;
        var foot = new RectangleF(area.X, parts[1].Y, area.Width, footH);
        string right = changed?.ChangedLap is { } lap ? PooledText.Number("L", lap) : "";
        float rw = right.Length > 0 ? p.Measure(right, p.Small) + 2 : 0;
        if (rw > 0) p.Text(right, p.Small, p.P.Faint, new RectangleF(foot.Right - rw, foot.Y, rw, foot.Height), HAlign.Right, VAlign.Center, false);
        float fx = foot.X, limit = foot.Right - rw - 10 * d;
        if (n < _shown.Count)
        {
            for (int i = n; i < _shown.Count && fx < limit; i++)
            {
                var item = _shown[i];
                string label = i > n ? PooledText.Concat(" · ", item.Label, " ") : PooledText.Concat(item.Label, " ");
                float lw = p.Measure(label, p.Small), vw = p.Measure(ControlLog.Format(item.Value), p.SmallBold) + 1;
                if (fx + lw + vw > limit) break;
                p.Text(label, p.Small, p.P.Muted, new RectangleF(fx, foot.Y, lw + 1, foot.Height), HAlign.Left, VAlign.Center, false);
                p.Text(ControlLog.Format(item.Value), p.SmallBold, p.P.Heading, new RectangleF(fx + lw, foot.Y, vw, foot.Height), HAlign.Left, VAlign.Center, false);
                fx += lw + vw;
            }
        }
        else
        {
            int changes = log.ChangesOnLap(v.Frame.Lap ?? changed?.ChangedLap);
            string text = changes > 0 ? PooledText.Concat(RaceFormat.Count(changes, "change"), " this lap") : live && changed is null ? "No changes this lap" : "";
            p.Text(text, p.Small, p.P.Muted, new RectangleF(fx, foot.Y, Math.Max(0, limit - fx), foot.Height), HAlign.Left, VAlign.Center);
        }
    }

    private static RectangleF BodyPanel(HudPainter p)
    {
        p.Panel();
        return p.Bounds.Inflated(-6 * p.Density, -6 * p.Density);
    }

    private static void Compact(HudPainter p, ControlItem item)
    {
        float d = p.Density;
        p.Panel();
        var r = p.Bounds.Inflated(-10 * d, -4 * d);
        var big = p.Font(20f, true);
        string value = ControlLog.Format(item.Value), step = StepText(item);
        float lw = p.Measure(item.Label, p.Bold) + 8 * d, vw = p.Measure(value, big) + 8 * d, sw = p.Measure(step, p.SmallBold) + 2;
        float total = lw + vw + sw, x = r.X + Math.Max(0, (r.Width - total) / 2);
        p.Text(item.Label, p.Bold, p.P.Muted, new RectangleF(x, r.Y, Math.Min(lw, r.Width), r.Height), HAlign.Left, VAlign.Center);
        x += lw;
        if (x + vw - 8 * d > r.Right) return;
        p.Text(value, big, p.P.Heading, new RectangleF(x, r.Y, vw, r.Height), HAlign.Left, VAlign.Center, false);
        x += vw;
        if (x + sw <= r.Right + 1) p.Text(step, p.SmallBold, p.P.AccentText, new RectangleF(x, r.Y, sw, r.Height), HAlign.Left, VAlign.Center, false);
    }

    private static readonly string[] BigParts = { "value", "scale", "summary" };
    private static readonly string[] KnobParts = { "knob", "history" };

    private static void DrawBig(HudPainter p, HudView v, ControlLog log, ControlItem item, double age)
    {
        float d = p.Density;
        var area = p.Header(item.Name, StepText(item), p.P.AccentText);
        const float px = 50f;
        string value = ControlLog.Format(item.Value);
        var size = p.DisplaySize("88.8", px);
        float scaleH = 40 * d, footH = p.LineHeight(p.Small) + 6 * d;
        p.NeedBelow(area, 4 * d + size.Height + 10 * d + scaleH + footH);
        float valueH = 4 * d + size.Height + 10 * d;
        bool footShown = area.Bottom - footH >= area.Y + valueH;
        Span<RectangleF> parts = stackalloc RectangleF[3];
        p.Arrange(v, "controls.big", area, false, 0, BigParts, stackalloc float[] { valueH, 0, footShown ? footH : -1 }, parts);
        float y = parts[0].Y + 4 * d;
        var vs = p.DisplaySize(value, px);
        p.Display(value, px, Fresh(p, age), new RectangleF(area.X, y, vs.Width + 2, size.Height), HAlign.Left, VAlign.Bottom);
        if (item.Key == ControlLog.BrakeBiasKey)
            p.Text("%", p.Font(18f, true), p.P.Muted, new RectangleF(area.X + vs.Width + 6 * d, y + size.Height - p.LineHeight(p.Font(18f, true)), 30 * d, p.LineHeight(p.Font(18f, true))), HAlign.Left, VAlign.Bottom, false);
        var scale = new RectangleF(area.X + 4 * d, parts[1].Y, area.Width - 8 * d, scaleH);
        if (scaleH <= parts[1].Height + 1) Steps(p, item, scale);
        if (!footShown) return;
        var foot = new RectangleF(area.X, parts[2].Y, area.Width, footH);
        if (foot.Y > area.Y) p.Line(foot.X, MathF.Round(foot.Y) + 0.5f, foot.Right, MathF.Round(foot.Y) + 0.5f, p.P.Line);
        int changes = log.ChangesOnLap(item.ChangedLap);
        string left = changes > 0 ? PooledText.Concat(RaceFormat.Count(changes, "change"), " this lap") : "";
        string right = item.Previous is { } was ? PooledText.Concat("was ", ControlLog.Format(was)) : "";
        float rw = right.Length > 0 ? p.Measure(right, p.Small) + 2 : 0;
        p.Text(right, p.Small, p.P.Muted, new RectangleF(foot.Right - rw, foot.Y, rw, foot.Height), HAlign.Right, VAlign.Center, false);
        p.Text(left, p.Small, p.P.Muted, new RectangleF(foot.X, foot.Y, Math.Max(0, foot.Width - rw - 8 * d), foot.Height), HAlign.Left, VAlign.Center);
    }

    private static void Steps(HudPainter p, ControlItem item, RectangleF r)
    {
        float d = p.Density;
        double lo = Math.Min(item.Start, Math.Min(item.Value, item.Previous ?? item.Value));
        double hi = Math.Max(item.Start, Math.Max(item.Value, item.Previous ?? item.Value));
        double margin = Math.Max((hi - lo) * 0.25, Math.Abs(item.Step ?? 1) * 0.5);
        if (hi - lo < 1e-6) margin = 1;
        lo -= margin;
        hi += margin;
        float lineY = r.Y + 9 * d;
        float X(double value) => r.X + (float)((value - lo) / (hi - lo)) * r.Width;
        p.Fill(new RectangleF(r.X, lineY - d, r.Width, 2 * d), HudPalette.Alpha(p.P.LineStrong, 200));
        var tiny = p.Tiny;
        float lh = p.LineHeight(tiny);
        float sx = X(item.Start);
        p.Fill(new RectangleF(sx - 1.5f * d, lineY - 7 * d, 3 * d, 14 * d), p.P.Muted);
        if (item.Previous is { } was && Math.Abs(was - item.Start) > 1e-6)
            p.Fill(new RectangleF(X(was) - 1.5f * d, lineY - 5 * d, 3 * d, 10 * d), HudPalette.Alpha(p.P.Muted, 130));
        float nx = X(item.Value);
        p.Fill(new RectangleF(nx - 2.5f * d, lineY - 10 * d, 5 * d, 20 * d), p.P.Accent, p.CornerRadius(3));
        string start = PooledText.Concat("start ", ControlLog.Format(item.Start));
        float w = p.Measure(start, tiny) + 2;
        float lx = Math.Clamp(sx - w / 2, r.X, r.Right - w);
        p.Text(start, tiny, p.P.Muted, new RectangleF(lx, lineY + 11 * d, w, lh), HAlign.Left, VAlign.Top, false);
    }

    private static void DrawKnob(HudPainter p, HudView v, ControlLog log, ControlItem item, double age)
    {
        float d = p.Density;
        var area = p.Header(item.Name, StepText(item), p.P.AccentText);
        float rowH = p.LineHeight(p.SmallBold) + 6 * d;
        float knob = 112 * d;
        p.NeedBelow(area, Math.Max(knob, rowH * 4 + 2 * d));
        knob = Math.Min(knob, area.Height);
        bool rows = area.Width - knob - 6 * d >= 120 * d;
        Span<RectangleF> parts = stackalloc RectangleF[2];
        if (rows) p.Arrange(v, "controls.knob", area, true, 6 * d, KnobParts, stackalloc float[] { Math.Max(knob, float.Epsilon), 0 }, parts);
        var box = rows ? new RectangleF(parts[0].X, area.Y, knob, knob) : new RectangleF(area.X + (area.Width - knob) / 2, area.Y, knob, knob);
        p.Tile(box);
        KnobFace(p, log, item, box.Inflated(-4 * d, -4 * d), age);
        if (!rows) return;
        var list = parts[1];
        float lapW = 34 * d;
        p.Text(p.LabelText("Lap"), p.Label, p.P.Muted, new RectangleF(list.X + 6 * d, list.Y, lapW, rowH), HAlign.Left, VAlign.Center, false);
        p.Text(p.LabelText("Recent changes"), p.Label, p.P.Muted, new RectangleF(list.X + 6 * d + lapW, list.Y, list.Width - lapW - 6 * d, rowH), HAlign.Left, VAlign.Center);
        float y = list.Y + rowH;
        for (int i = 0; i < log.HistoryCount && y + rowH <= list.Bottom + 1; i++)
        {
            var c = log.History(i);
            var r = new RectangleF(list.X, y, list.Width, rowH);
            bool newest = i == 0;
            if (newest) p.Fill(r, p.Solid(HudPalette.Alpha(p.P.Player, 255), 160), p.CornerRadius(3));
            else p.Line(r.X, MathF.Round(r.Y) + 0.5f, r.Right, MathF.Round(r.Y) + 0.5f, p.P.Line);
            var fg = newest ? p.P.PlayerText : p.P.Text;
            p.Text(c.Lap is { } lap ? PooledText.Number("", lap) : RaceFormat.Missing, p.SmallBold, newest ? fg : p.P.Muted, new RectangleF(r.X + 6 * d, r.Y, lapW, rowH), HAlign.Left, VAlign.Center, false);
            string text = PooledText.Concat(c.Item.Label, " ", PooledText.Concat(ControlLog.Format(c.From), " → ", ControlLog.Format(c.To)));
            p.Text(text, p.SmallBold, fg, new RectangleF(r.X + 6 * d + lapW, r.Y, r.Width - lapW - 12 * d, rowH), HAlign.Left, VAlign.Center);
            y += rowH;
        }
    }

    private static void KnobFace(HudPainter p, ControlLog log, ControlItem item, RectangleF box, double age)
    {
        float d = p.Density;
        float cx = box.X + box.Width / 2, cy = box.Y + box.Height / 2 + 3 * d;
        float outer = Math.Min(box.Width, box.Height) / 2 - 2 * d, inner = outer - 11 * d, face = inner - 6 * d;
        double step = item.Step ?? 0;
        double unit = log.SmallestStep(item);
        int lit = step == 0 ? 0 : (int)Math.Clamp(Math.Round(Math.Abs(step) / (unit > 0 ? unit : Math.Abs(step))), 1, KnobMaxLit);
        int dir = step > 0 ? 1 : -1;
        int middle = KnobTicks / 2;
        var dark = p.Solid(p.Sunken(0.35), 230);
        var color = p.P.Accent;
        for (int i = 0; i < KnobTicks; i++)
        {
            double angle = (-225 + 270.0 * i / (KnobTicks - 1)) * Math.PI / 180;
            float ca = (float)Math.Cos(angle), sa = (float)Math.Sin(angle);
            int off = i - middle;
            var c = off == 0 ? p.P.Muted : lit > 0 && Math.Sign(off) == dir && Math.Abs(off) <= lit ? color : dark;
            p.Line(cx + ca * inner, cy + sa * inner, cx + ca * outer, cy + sa * outer, c, 5 * d);
        }
        p.Dot(cx, cy, face, dark);
        p.G.DrawEllipse(p.Pen(HudPalette.Alpha(p.P.LineStrong, 200), d), cx - face, cy - face, face * 2, face * 2);
        string value = ControlLog.Format(item.Value);
        float px = Math.Min(30f, face * 1.1f / d);
        float room = face * 1.35f;
        float wide = p.DisplaySize(value, px).Width;
        if (wide > room && wide > 0) px = Math.Max(10f, px * room / wide);
        var tiny = p.Tiny;
        float lh = p.LineHeight(tiny);
        bool was = item.Previous is not null && face >= 22 * d;
        p.Display(value, px, Fresh(p, age), new RectangleF(cx - face, cy - face, face * 2, face * 2 - (was ? lh * 0.6f : 0)), HAlign.Center, VAlign.Center);
        if (was) p.Text(PooledText.Concat("was ", ControlLog.Format(item.Previous!.Value)), tiny, p.P.Muted, new RectangleF(cx - face, cy + face * 0.42f, face * 2, lh), HAlign.Center, VAlign.Top, false);
    }
}
