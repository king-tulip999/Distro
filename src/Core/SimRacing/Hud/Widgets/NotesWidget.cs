using System.Drawing;
using Core.Config;
using Core.SimRacing.Race;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Hud.Widgets;

internal sealed class NotesWidget : HudWidget
{
    public const string Countdown = "countdown", Ticker = "ticker", Strip = "strip", List = "list";
    private const float K = 0.9f;
    internal const double StripSpan = 1200;
    internal const int ListRows = 3;

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("style", "Style", Countdown,
            new HudChoice(Countdown, "Countdown", "The next corner large with its distance and a bar closing in, the corner after it underneath"),
            new HudChoice(Ticker, "Ticker", "One slim row: the next corner, its note and its distance"),
            new HudChoice(Strip, "Approach strip", "The corners ahead sliding toward you along a line, the next note under it"),
            new HudChoice(List, "Distance list", "The next three corners with their distances, further ones fading")),
        TitleRow,
        HudOption.Int("ahead", "Corner notes from", 300, 50, 2000, Services.CapsUnits.M, "How far before the corner its note lights up"),
        HudOption.Bool("general", "Notes for the whole track", true),
        HudOption.Bool("onlyNext", "Only the next corner's note", false),
    };

    public static bool Moves(HudOptions o) => o.Choice("style") != List;

    private static readonly List<TrackNote> Samples = new()
    {
        new TrackNote { Pct = 0.08, Turn = "T1", Text = "Brake at the 100 board, trail into the apex" },
        new TrackNote { Pct = 0.42, Turn = "T5", Text = "Stay off the exit curb: it counts as off" },
        new TrackNote { Pct = 0.71, Turn = "T9", Text = "Wait for the rear to rotate before throttle" },
        new TrackNote { Text = "Bias 52.5 once the fuel is down" },
    };

    private readonly List<(TrackNote Note, double Metres)> _corners = new();
    private readonly List<TrackNote> _general = new();
    private readonly PointF[] _car = new PointF[3];
    private readonly Dictionary<(string, int, Font), (string Head, string Tail)> _splits = new();
    private (string? Turn, string? Distance, string Text) _stripRight = (null, null, "");

    internal static double Rounded(double metres) => Math.Max(0, Math.Round(metres / 10) * 10);

    internal static float Closing(double metres, double ahead) => ahead <= 0 ? 0 : (float)Math.Clamp(1 - metres / (ahead * 2), 0, 1);

    internal static float Closed(double metres, double ahead) => ahead <= 0 || metres > ahead ? 0 : (float)Math.Clamp(1 - metres / ahead, 0, 1);

    internal static int ListAlpha(double metres, double ahead, bool lit) => lit ? 255 : (int)Math.Round(255 * Math.Max(0.45, 1 - Math.Max(0, metres - ahead) / 2400));

    internal static int StripAlpha(double metres) => (int)Math.Round(255 * Math.Max(0.35, 1 - metres / StripSpan * 0.65));

    internal static (string Head, string Tail) Split(string text, float width, Func<string, float> measure)
    {
        if (measure(text) <= width) return (text, "");
        int cut = -1;
        for (int i = text.IndexOf(' '); i > 0; i = text.IndexOf(' ', i + 1))
        {
            if (measure(text[..i]) > width) break;
            cut = i;
        }
        return cut <= 0 ? (text, "") : (text[..cut], text[(cut + 1)..].TrimStart());
    }

    private void Collect(HudView v)
    {
        _corners.Clear();
        _general.Clear();
        var f = v.Frame;
        var me = f.Session.Player;
        double length = (f.Session.TrackLengthKm ?? 0) * 1000;
        double at = f.LapDistPct is { } p && double.IsFinite(p) ? p : 0;
        var notes = v.Settings.NotesFor(f.Session.TrackDisplayName, f.Session.TrackConfig, me is null ? "" : me.CarName.Length > 0 ? me.CarName : me.CarShortName).ToList();
        if (notes.Count == 0 && f.Simulated) notes = Samples;
        foreach (var n in notes)
        {
            if (n.Pct is { } pct) _corners.Add((n, ((pct - at) % 1 + 1) % 1 * length));
            else if (v.Options.Bool("general")) _general.Add(n);
        }
        _corners.Sort(static (a, b) => a.Metres.CompareTo(b.Metres));
    }

    public override bool HasContent(HudView v)
    {
        if (v.Frame.Status != SimStatus.Connected) return false;
        Collect(v);
        return _corners.Count > 0 || _general.Count > 0;
    }

    public override void Draw(HudPainter p, HudView v)
    {
        string style = v.Options.Choice("style");
        if (DrawWaiting(p, v, style == Ticker ? null : "Notes")) return;
        Collect(v);
        if (_corners.Count == 0 && _general.Count == 0)
        {
            DrawEmpty(p, style);
            return;
        }
        switch (style)
        {
            case Ticker: DrawTicker(p, v); break;
            case Strip: DrawStrip(p, v); break;
            case List: DrawList(p, v); break;
            default: DrawCountdown(p, v); break;
        }
    }

    private static string Turn(TrackNote note, HudView v) => note.Turn ?? v.Race.Turns.Where(note.Pct!.Value, caps: true);

    private static string Distance(double metres, HudView v) => RaceFormat.Distance(Rounded(metres), v.Units);

    private int Shown(HudView v, int most) => Math.Min(v.Options.Bool("onlyNext") ? 1 : most, _corners.Count);

    private static void DrawEmpty(HudPainter p, string style)
    {
        float s = K * p.Density;
        RectangleF area;
        if (style == Ticker)
        {
            p.Panel();
            area = p.Bounds.Inflated(-12 * s, 0);
        }
        else area = p.Header("Notes");
        p.Text("No notes for this track yet", p.Small, p.P.Faint, area, HAlign.Center, VAlign.Center);
        p.NeedBelow(area, 40 * s);
    }

    private (string Head, string Tail) Lines(HudPainter p, string text, Font font, float width)
    {
        var key = (text, (int)width, font);
        if (_splits.TryGetValue(key, out var done)) return done;
        if (_splits.Count >= 64) _splits.Clear();
        return _splits[key] = Split(text, width, t => p.Measure(t, font));
    }

    private float TwoLines(HudPainter p, string text, Font font, Color color, RectangleF box, float lineH, bool centered)
    {
        var (first, rest) = Lines(p, text, font, box.Width);
        int count = rest.Length > 0 ? 2 : 1;
        float top = centered ? box.Y + (box.Height - count * lineH) / 2 : box.Y;
        p.Text(first, font, color, new RectangleF(box.X, top, box.Width, lineH), HAlign.Left);
        if (count == 2) p.Text(rest, font, color, new RectangleF(box.X, top + lineH, box.Width, lineH), HAlign.Left);
        return count * lineH;
    }

    private static void Rule(HudPainter p, float x1, float x2, float y) => p.Fill(new RectangleF(x1, MathF.Round(y), x2 - x1, 1), HudPalette.Alpha(p.P.Accent, 115));

    private float DrawGeneral(HudPainter p, RectangleF inner, float y, float s, bool rule)
    {
        if (_general.Count == 0) return y;
        if (rule)
        {
            Rule(p, inner.X, inner.Right, y);
            y += 1 + 10 * s;
        }
        float lineH = 18 * s;
        for (int i = 0; i < _general.Count; i++)
        {
            if (i > 0) y += 6 * s;
            p.Text(_general[i].Text, p.Small, p.P.Text, new RectangleF(inner.X, y, inner.Width, lineH), HAlign.Left);
            y += lineH;
        }
        return y;
    }

    private void DrawCountdown(HudPainter p, HudView v)
    {
        float s = K * p.Density;
        var body = p.Header("Notes").Inflated(8 * p.Density, 6 * p.Density);
        var inner = body.Inflated(-12 * s, -12 * s);
        float y = inner.Y;
        int ahead = v.Options.Int("ahead");
        if (_corners.Count > 0)
        {
            var (note, metres) = _corners[0];
            bool lit = metres <= ahead;
            float tile = 44 * s;
            var tileBox = new RectangleF(inner.X, y, tile, tile);
            p.Fill(tileBox, lit ? p.P.Accent : p.P.SurfaceAlt, 8 * s);
            string turn = Turn(note, v);
            var turnColor = lit ? p.P.OnAccent : p.P.Heading;
            if (p.DisplaySize(turn, 20).Width <= tile - 6 * s) p.Display(turn, 20, turnColor, tileBox, HAlign.Center, VAlign.Center);
            else p.Text(turn, p.SmallBold, turnColor, tileBox.Inflated(-2 * s, 0), HAlign.Center);
            string dist = Distance(metres, v);
            var distFont = p.Font(16f, true);
            float distW = Math.Max(62 * s, p.Measure(dist, distFont));
            var distBox = new RectangleF(inner.Right - distW, y, distW, tile);
            p.Text(dist, distFont, lit ? p.P.AccentText : p.P.Faint, distBox, HAlign.Right);
            float textX = tileBox.Right + 12 * s;
            var textBox = new RectangleF(textX, y, Math.Max(0, distBox.Left - 12 * s - textX), tile);
            TwoLines(p, note.Text, lit ? p.Bold : p.Body, lit ? p.P.Heading : p.P.Muted, textBox, 18.75f * s, true);
            y += tile + 10 * s;
            float barH = 3 * s;
            var track = new RectangleF(inner.X, y, inner.Width, barH);
            p.Fill(track, p.P.InputTrack, barH / 2);
            float done = Closing(metres, ahead);
            if (done > 0) p.Fill(new RectangleF(track.X, track.Y, track.Width * done, barH), lit ? p.P.Accent : p.P.Highlight ?? p.P.Slate, barH / 2);
            y += barH + 10 * s;
            if (Shown(v, 2) > 1)
            {
                var (next, nextMetres) = _corners[1];
                float lineH = 18 * s;
                string nextDist = Distance(nextMetres, v);
                float nextW = p.Measure(nextDist, p.Small) + 2;
                p.Text(Turn(next, v), p.SmallBold, p.P.Muted, new RectangleF(inner.X, y, 24 * s, lineH), HAlign.Left);
                float nx = inner.X + 24 * s + 8 * s;
                p.Text(next.Text, p.Small, p.P.Faint, new RectangleF(nx, y, Math.Max(0, inner.Right - nextW - 8 * s - nx), lineH), HAlign.Left);
                p.Text(nextDist, p.Small, p.P.Faint, new RectangleF(inner.Right - nextW, y, nextW, lineH), HAlign.Right);
                y += lineH + 10 * s;
            }
        }
        y = _general.Count > 0 ? DrawGeneral(p, inner, y, s, _corners.Count > 0) : y - 10 * s;
        p.NeedBelow(inner, y - inner.Y);
    }

    private void DrawTicker(HudPainter p, HudView v)
    {
        float s = K * p.Density, w = p.Bounds.Width, y = 0;
        int ahead = v.Options.Int("ahead");
        if (_corners.Count > 0)
        {
            var (note, metres) = _corners[0];
            bool lit = metres <= ahead;
            var row = new RectangleF(0, 0, w, 40 * s);
            p.Panel(row);
            string turn = Turn(note, v);
            float chipH = 24 * s, chipW = Math.Max(34 * s, p.Measure(turn, p.SmallBold) + 18 * s);
            var chip = new RectangleF(8 * s, row.Y + (row.Height - chipH) / 2, chipW, chipH);
            p.Fill(chip, lit ? p.P.Accent : p.P.SurfaceAlt, chipH / 2);
            p.Text(turn, p.SmallBold, lit ? p.P.OnAccent : p.P.Muted, chip, HAlign.Center);
            string dist = Distance(metres, v);
            var distFont = p.Font(12.5f, true);
            float distW = p.Measure(dist, distFont) + 2;
            var distBox = new RectangleF(row.Right - 12 * s - distW, row.Y, distW, row.Height);
            p.Text(dist, distFont, lit ? p.P.AccentText : p.P.Faint, distBox, HAlign.Right);
            float tx = chip.Right + 10 * s;
            p.Text(note.Text, p.Font(12.5f, lit), lit ? p.P.Heading : p.P.Muted, new RectangleF(tx, row.Y, Math.Max(0, distBox.Left - 10 * s - tx), row.Height), HAlign.Left);
            float done = Closed(metres, ahead);
            if (done > 0)
            {
                var clip = p.ClipEnds(row, p.Look.Corner, false, true);
                p.Fill(new RectangleF(0, row.Bottom - 2 * s, w * done, 2 * s), p.P.Accent);
                p.Unclip(clip);
            }
            y = row.Bottom + 8 * s;
        }
        for (int i = 0; i < _general.Count; i++)
        {
            var row = new RectangleF(0, y, w, 32 * s);
            p.Panel(row);
            p.Mark(12 * s, row.Y + row.Height / 2);
            float tx = 12 * s + p.MarkSize + 10 * s;
            p.Text(_general[i].Text, p.Small, p.P.Text, new RectangleF(tx, row.Y, Math.Max(0, w - 12 * s - tx), row.Height), HAlign.Left);
            y = row.Bottom + 8 * s;
        }
        p.Need(w, y - 8 * s);
    }

    private void DrawStrip(HudPainter p, HudView v)
    {
        float s = K * p.Density;
        int ahead = v.Options.Int("ahead");
        bool any = _corners.Count > 0;
        bool lit = any && _corners[0].Metres <= ahead;
        string? right = null;
        if (any)
        {
            string turn = Turn(_corners[0].Note, v), dist = Distance(_corners[0].Metres, v);
            if (!ReferenceEquals(turn, _stripRight.Turn) || !ReferenceEquals(dist, _stripRight.Distance)) _stripRight = (turn, dist, turn + " · " + dist);
            right = _stripRight.Text;
        }
        var body = p.Header("Notes", right, lit ? p.P.AccentText : p.P.Faint).Inflated(8 * p.Density, 6 * p.Density);
        var inner = RectangleF.FromLTRB(body.X + 12 * s, body.Y + 10 * s, body.Right - 12 * s, body.Bottom - 12 * s);
        float y = inner.Y;
        if (any)
        {
            var strip = new RectangleF(inner.X, y, inner.Width, 40 * s);
            float left = strip.X + 8 * s, span = Math.Max(1, strip.Width - 16 * s);
            p.Fill(new RectangleF(left, strip.Y + 27 * s, (float)(Math.Min(ahead, StripSpan) / StripSpan) * span, 6 * s), HudPalette.Alpha(p.P.Accent, 56), 3 * s);
            p.Fill(new RectangleF(left, strip.Y + 29 * s, span, 2 * s), p.P.LineStrong);
            int shown = Shown(v, _corners.Count);
            for (int i = shown - 1; i >= 0; i--)
            {
                var (note, metres) = _corners[i];
                if (metres > StripSpan) continue;
                bool on = i == 0 && lit;
                int alpha = StripAlpha(metres);
                float x = left + (float)(metres / StripSpan) * span;
                p.Text(Turn(note, v), p.SmallBold, HudPalette.Alpha(on ? p.P.AccentText : p.P.Muted, alpha), new RectangleF(x - 16 * s, strip.Y + 2 * s, 32 * s, 16 * s), HAlign.Center, VAlign.Top, false);
                p.Fill(new RectangleF(x - 5 * s, strip.Y + 25 * s, 10 * s, 10 * s), HudPalette.Alpha(on ? p.P.Accent : p.P.Faint, alpha), 5 * s);
            }
            _car[0] = new PointF(strip.X + 2 * s, strip.Y + 23 * s);
            _car[1] = new PointF(strip.X + 2 * s, strip.Y + 37 * s);
            _car[2] = new PointF(strip.X + 14 * s, strip.Y + 30 * s);
            p.G.FillPolygon(p.Brush(p.P.Heading), _car);
            y += 40 * s + 10 * s;
            float used = TwoLines(p, _corners[0].Note.Text, lit ? p.Bold : p.Body, lit ? p.P.Heading : p.P.Muted, new RectangleF(inner.X, y, inner.Width, 38 * s), 18.75f * s, false);
            y += Math.Max(38 * s, used) + 10 * s;
        }
        y = _general.Count > 0 ? DrawGeneral(p, inner, y, s, any) : y - 10 * s;
        p.NeedBelow(inner, y - inner.Y);
    }

    private void DrawList(HudPainter p, HudView v)
    {
        float s = K * p.Density;
        int ahead = v.Options.Int("ahead");
        var body = p.Header("Notes").Inflated(8 * p.Density, 6 * p.Density);
        var inner = RectangleF.FromLTRB(body.X + 8 * s, body.Y + 6 * s, body.Right - 8 * s, body.Bottom - 8 * s);
        float y = inner.Y, rowH = 30 * s, gap = 3 * s;
        int shown = Shown(v, ListRows);
        float distW = 58 * s;
        for (int i = 0; i < shown; i++) distW = Math.Max(distW, p.Measure(Distance(_corners[i].Metres, v), p.SmallBold) + 2);
        for (int i = 0; i < shown; i++)
        {
            var (note, metres) = _corners[i];
            bool on = i == 0 && metres <= ahead;
            int alpha = ListAlpha(metres, ahead, on);
            var row = new RectangleF(inner.X, y, inner.Width, rowH);
            if (on) p.Fill(row, HudPalette.Alpha(p.P.Accent, 89), 5 * s);
            var cells = row.Inflated(-8 * s, 0);
            p.Text(Turn(note, v), p.SmallBold, HudPalette.Alpha(on ? p.P.Heading : p.P.Muted, alpha), new RectangleF(cells.X, row.Y, 26 * s, rowH), HAlign.Left);
            p.Text(Distance(metres, v), p.SmallBold, HudPalette.Alpha(on ? p.P.Heading : p.P.Faint, alpha), new RectangleF(cells.Right - distW, row.Y, distW, rowH), HAlign.Right);
            float tx = cells.X + 26 * s + 8 * s;
            p.Text(note.Text, p.Font(12.5f, on), HudPalette.Alpha(on ? p.P.Heading : p.P.Text, alpha), new RectangleF(tx, row.Y, Math.Max(0, cells.Right - distW - 8 * s - tx), rowH), HAlign.Left);
            y += rowH + gap;
        }
        if (_general.Count > 0)
        {
            if (shown > 0)
            {
                y += 3 * s;
                Rule(p, inner.X + 2 * s, inner.Right - 2 * s, y);
                y += 1 + 3 * s + gap;
            }
            float lineH = 26 * s;
            for (int i = 0; i < _general.Count; i++)
            {
                p.Text(_general[i].Text, p.Small, p.P.Text, new RectangleF(inner.X + 8 * s, y, Math.Max(0, inner.Width - 16 * s), lineH), HAlign.Left);
                y += lineH + gap;
            }
        }
        p.NeedBelow(inner, y - gap - inner.Y);
    }
}
