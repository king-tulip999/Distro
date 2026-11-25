using System.Drawing;
using Core.SimRacing.Race;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Hud.Widgets;

internal sealed class SessionWidget : HudWidget
{
    public const string Tall = "tall", StripStyle = "strip", PositionFirst = "position", Grid = "grid";
    private const string LongClock = "8:88:88", LongLaps = "888 / 888", LongPosition = "P88";
    private const float CapRatio = 0.7f;

    private readonly List<(string Id, string Label, string Value)> _cells = new();
    private readonly List<int> _rest = new();
    private readonly Dictionary<string, string> _eights = new(StringComparer.Ordinal);
    private readonly float[] _miniWidths = new float[3];

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("style", "Style", Tall,
            new HudChoice(Tall, "Tall", "A narrow column: the first value big, the rest in rows under it"),
            new HudChoice(StripStyle, "Strip", "One low row for the top or bottom edge of the screen"),
            new HudChoice(PositionFirst, "Position first", "Your position big, with time and laps left beside it"),
            new HudChoice(Grid, "Grid", "The first value big beside a grid of the rest")),
        TitleRow,
        HudOption.Columns("fields", "Shows", "remain,lapsleft,lap,cpos",
            new HudChoice("remain", "Time left"), new HudChoice("lapsleft", "Laps left", "Timed races: estimated from the pace"), new HudChoice("elapsed", "Time elapsed"),
            new HudChoice("lap", "Lap"), new HudChoice("pos", "Position"), new HudChoice("cpos", "Position in class"), new HudChoice("phase", "Phase"), new HudChoice("track", "Track"),
            new HudChoice("temps", "Air / track temperature"), new HudChoice("inc", "Incidents"), new HudChoice("clock", "Time of day"),
            new HudChoice("finish", "Estimated finish (time of day)"),
            new HudChoice("irchange", "iRating gain / loss (est.)", "Races: what you'd gain or lose if the race finished in this order"),
            new HudChoice("offtracks", "Off-tracks", "Every time all four wheels left the track this session, and where the last one was"),
            new HudChoice("consistency", "Consistency", "Half the spread of your last 5 clean laps"),
            new HudChoice("trend", "Track temperature trend", "Change over the last 10 minutes (needs 5 minutes of readings)")),
        HudOption.Bool("progress", "Progress bar", true),
    };

    public override void Draw(HudPainter p, HudView v)
    {
        var f = v.Frame;
        if (f.Status != SimStatus.Connected)
        {
            var a = p.Header("Session");
            p.Text(f.Status == SimStatus.Waiting ? "iRacing is running · no session yet" : "Waiting for iRacing", p.Body, p.P.Muted, a, HAlign.Center, VAlign.Center);
            return;
        }
        var session = f.CurrentSession;
        string title = session?.Name is { Length: > 0 } n ? n : v.Race.SessionLabel;
        string track = f.Session.TrackDisplayName;
        Collect(v);
        bool bar = v.Options.Bool("progress");
        double? done = session?.Laps is > 0 and var total && f.LapCompleted is { } lc ? (lc + (f.LapDistPct ?? 0)) / total
            : f.SessionTimeTotal is > 0 && f.SessionTimeRemain is { } rem ? 1 - rem / f.SessionTimeTotal.Value : null;
        switch (v.Options.Choice("style"))
        {
            case StripStyle: DrawStrip(p, v, title, track, bar, done); break;
            case PositionFirst: DrawPosition(p, v, title, track, bar, done); break;
            case Grid: DrawGrid(p, v, title, track, bar, done); break;
            default: DrawTall(p, v, title, track, bar, done); break;
        }
    }

    private void Collect(HudView v)
    {
        var f = v.Frame;
        var session = f.CurrentSession;
        int me = f.PlayerCarIdx;
        var cells = _cells;
        cells.Clear();
        foreach (var id in v.Options.Columns("fields"))
        {
            switch (id)
            {
                case "remain": cells.Add((id, "TIME LEFT", Strips.TimeLeft(v) ?? RaceFormat.Missing)); break;
                case "lapsleft": cells.Add((id, "LAPS LEFT", Strips.LapsLeft(v) ?? RaceFormat.Missing)); break;
                case "elapsed": cells.Add((id, "ELAPSED", RaceFormat.Clock(f.SessionTime))); break;
                case "lap": cells.Add((id, "LAP", f.Lap is { } l ? (session?.Laps is > 0 and var total ? PooledText.Number("", l, " / ", total) : l.ToString()) : RaceFormat.Missing)); break;
                case "pos": cells.Add((id, "POSITION", (uint)me < TelemetryFrame.MaxCars && f.CarPosition[me] > 0 ? RaceFormat.Pos(f.CarPosition[me]) : RaceFormat.Missing)); break;
                case "cpos":
                    cells.Add((id, PooledText.Concat("In ", f.Session.Player?.ClassLabel is { Length: > 0 } cn ? cn : "class"),
                        v.Race.StandingOf(me)?.ClassPosition is > 0 and var cp ? RaceFormat.Pos(cp) : RaceFormat.Missing));
                    break;
                case "phase": cells.Add((id, "PHASE", RaceControl.SessionPhaseLabel(f.Phase) is { Length: > 0 } ph ? ph : RaceFormat.Missing)); break;
                case "track": cells.Add((id, "TRACK", f.Session.TrackConfig.Length > 0 ? f.Session.TrackConfig : f.Session.TrackDisplayName)); break;
                case "temps": cells.Add((id, "AIR / TRACK", PooledText.Concat(RaceFormat.Temp(f.AirTemp, v.Units), " / ", RaceFormat.Temp(f.TrackTemp, v.Units)))); break;
                case "inc": cells.Add((id, "INCIDENTS", f.Incidents is { } i ? (f.Session.IncidentLimit is { } lim ? PooledText.Number("", i, "x / ", lim, "x") : PooledText.Number("", i, "x")) : RaceFormat.Missing)); break;
                case "clock": cells.Add((id, "TIME", RaceFormat.TimeOfDay(DateTime.Now, v.Settings.Clock24h))); break;
                case "finish":
                    double? togo = v.Race.Fuel?.LapsToGo ?? (f.SessionKind == SessionKind.Race ? v.Race.LapsToGo(f) : null);
                    double? left = session?.Laps is > 0 ? (togo is { } lt && LapMath.Stats(v.Race.Laps.Laps, 5)?.Average is { } avg ? lt * avg : null) : f.SessionTimeRemain;
                    cells.Add((id, "FINISH ~", left is { } s ? RaceFormat.TimeOfDay(DateTime.Now.AddSeconds(s), v.Settings.Clock24h) : RaceFormat.Missing));
                    break;
                case "irchange":
                    cells.Add((id, "IR ± EST.", v.Race.IRatingChangeOf(me) is { } ir ? RaceFormat.Signed(Math.Round(ir), "0") : RaceFormat.Missing));
                    break;
                case "offtracks":
                    var limits = v.Race.TrackLimits;
                    cells.Add((id, "OFF-TRACKS", limits.Count == 0 ? "0" : limits.Offs[^1].Pct >= 0 ? $"{limits.Count} · {v.Race.Turns.Where(limits.Offs[^1].Pct, caps: true)}" : limits.Count.ToString()));
                    break;
                case "consistency":
                    cells.Add((id, "CONSISTENCY", RaceFormat.Spread(LapMath.Stats(v.Race.Laps.Laps, 5) is { Count: >= 3 } cs ? cs.Spread : null)));
                    break;
                case "trend":
                    cells.Add((id, "TRACK 10 MIN", v.Race.Weather.TrackPerWindow is { } tr ? (tr < 0 ? "−" : "+") + RaceFormat.TempDelta(Math.Abs(tr), v.Units) : RaceFormat.Missing));
                    break;
            }
        }
    }

    internal static string LongestOf(string id, string value) => id switch
    {
        "remain" or "elapsed" => LongClock,
        "lap" when value.Contains('/') => LongLaps,
        "pos" or "cpos" => LongPosition,
        _ => "",
    };

    internal static string Eights(string value)
    {
        Span<char> chars = value.Length <= 64 ? stackalloc char[value.Length] : new char[value.Length];
        bool changed = false;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsAsciiDigit(c) && c != '8') { c = '8'; changed = true; }
            chars[i] = c;
        }
        return changed ? new string(chars) : value;
    }

    private string Widest(string value)
    {
        if (_eights.TryGetValue(value, out var known)) return known;
        if (_eights.Count >= 256) _eights.Clear();
        return _eights[value] = Eights(value);
    }

    private float ValueWidth(HudPainter p, string id, string value, Font font)
    {
        float w = p.Measure(Widest(value), font);
        string longest = LongestOf(id, value);
        return longest.Length > 0 ? Math.Max(w, p.Measure(longest, font)) : w;
    }

    private float DisplayWidth(HudPainter p, string id, string value, float px)
    {
        float w = p.DisplaySize(Widest(value), px).Width;
        string longest = LongestOf(id, value);
        return longest.Length > 0 ? Math.Max(w, p.DisplaySize(longest, px).Width) : w;
    }

    private static float LabelWidth(HudPainter p, string label) => p.Measure(p.LabelText(label), p.Label);

    private static float Cap(Font font) => font.Size * CapRatio;

    private static float DisplayCap(HudPainter p, float px) => p.DisplaySize("8", px).Height;

    private static Color Rule(HudPainter p) => HudPalette.Alpha(p.P.LineStrong, 110);

    private static void Ink(HudPainter p, string text, Font font, Color color, float x, float capTop, float width, HAlign align = HAlign.Left, bool ellipsis = true) =>
        p.Text(text, font, color, new RectangleF(x, capTop + Cap(font) - p.Ascent(font), width, p.LineHeight(font)), align, VAlign.Top, ellipsis);

    private static void Label(HudPainter p, string label, float x, float capTop, float width, HAlign align = HAlign.Left) =>
        Ink(p, p.LabelText(label), p.Label, p.LabelColor, x, capTop, width, align);

    private static float TitleWidth(HudPainter p, string title) => p.MarkSize + 6 * p.Density + p.Measure(p.LabelText(title), p.Label);

    private static void Title(HudPainter p, string title, float x, float capTop, float width, HAlign align)
    {
        float tw = Math.Min(width, TitleWidth(p, title));
        float left = align == HAlign.Right ? x + width - tw : x;
        p.Mark(left, capTop + Cap(p.Label) / 2);
        float textX = left + p.MarkSize + 6 * p.Density;
        Ink(p, p.LabelText(title), p.Label, p.TitleColor, textX, capTop, Math.Max(0, x + width - textX));
    }

    private static void Progress(HudPainter p, RectangleF r, double? done)
    {
        p.Region("progress", r);
        if (done is { } d) p.Bar(r, d, p.P.Accent, p.Solid(p.Raised(0.10), 130));
    }

    private static readonly string[] TallParts = { "title", "hero", "progress", "rows" };
    private static readonly string[] StripParts = { "fields", "title" };
    private static readonly string[] StripStackParts = { "body", "progress" };
    private static readonly string[] GridParts = { "fields", "progress" };
    private static readonly string[] PositionRightParts = { "main", "progress", "more" };

    private void DrawTall(HudPainter p, HudView v, string title, string track, bool bar, double? done)
    {
        const float HeroPx = 46;
        float d = p.Density, pad = 12 * d, gap = 10 * d, rowPad = 7 * d, barH = 3 * d;
        var lf = p.Label;
        var vf = p.Font(15, true);
        bool titled = p.TitleRowShown;
        float lcap = Cap(lf), hcap = DisplayCap(p, HeroPx), scap = Cap(p.Small), rowH = Cap(vf) + rowPad * 2;
        var cells = _cells;
        int rows = Math.Max(0, cells.Count - 1);

        float inner = titled ? TitleWidth(p, title) : 0;
        if (cells.Count > 0) inner = Math.Max(inner, Math.Max(DisplayWidth(p, cells[0].Id, cells[0].Value, HeroPx), LabelWidth(p, cells[0].Label)));
        for (int i = 1; i < cells.Count; i++)
            inner = Math.Max(inner, LabelWidth(p, cells[i].Label) + 14 * d + ValueWidth(p, cells[i].Id, cells[i].Value, vf));
        float w = MathF.Ceiling(Math.Max(216 * d, inner + pad * 2));

        float h = pad;
        if (titled) h += lcap + 6 * d + scap + gap + 2 * d;
        if (cells.Count > 0) h += hcap + 8 * d + lcap + gap;
        if (bar) h += barH + gap;
        h += rows > 0 ? rows * rowH - rowPad : -gap;
        h += pad;
        p.Need(w, h);
        p.Panel();

        float x = pad, cw = w - pad * 2, y;
        Span<RectangleF> parts = stackalloc RectangleF[4];
        p.Arrange(v, "session.tall", new RectangleF(x, pad, cw, h - pad * 2), false, gap, TallParts,
            stackalloc float[] { titled ? lcap + 6 * d + scap + 2 * d : -1, cells.Count > 0 ? hcap + 8 * d + lcap : -1, bar ? barH : -1, rows > 0 ? rows * rowH - rowPad : -1 }, parts);
        if (titled)
        {
            y = parts[0].Y;
            Title(p, title, x, y, cw, HAlign.Left);
            Ink(p, track, p.Small, p.P.Muted, x, y + lcap + 6 * d, cw);
        }
        if (cells.Count > 0)
        {
            y = parts[1].Y;
            var (id, label, value) = cells[0];
            p.Display(value, HeroPx, p.P.Heading, new RectangleF(x, y, cw, hcap), HAlign.Left, VAlign.Bottom);
            Label(p, label, x, y + hcap + 8 * d, cw);
            p.Item("fields", id, new RectangleF(x, y, cw, hcap + 8 * d + lcap));
        }
        if (bar) Progress(p, parts[2], done);
        y = parts[3].Y;
        for (int i = 1; i < cells.Count; i++)
        {
            var (id, label, value) = cells[i];
            p.Line(x, y, x + cw, y, Rule(p));
            float capTop = y + rowPad;
            Label(p, label, x, capTop + (Cap(vf) - lcap) / 2, cw);
            Ink(p, value, vf, p.P.Heading, x, capTop, cw, HAlign.Right, false);
            p.Item("fields", id, new RectangleF(x, y, cw, rowH));
            y += rowH;
        }
    }

    private void DrawStrip(HudPainter p, HudView v, string title, string track, bool bar, double? done)
    {
        const float HeroPx = 30;
        float d = p.Density, pad = 12 * d, side = 14 * d, top = 12 * d, labelGap = 7 * d, barH = 3 * d;
        var lf = p.Label;
        var vf = p.Font(18, true);
        bool titled = p.TitleRowShown;
        var cells = _cells;
        float lcap = Cap(lf), vcap = Math.Max(DisplayCap(p, HeroPx), Cap(vf));
        float scap = Cap(p.Small);

        float fieldsW = 0;
        for (int i = 0; i < cells.Count; i++)
            fieldsW += Column(p, i, vf, HeroPx) + (i > 0 ? side * 2 : 0);
        float w = pad * 2 + fieldsW;
        float place = titled ? Math.Max(TitleWidth(p, title), Math.Min(p.Measure(track, p.Small), 200 * d)) : 0;
        if (titled) w += place + (cells.Count > 0 ? side * 2 : 0);
        w = MathF.Ceiling(w);
        float body = top + vcap + labelGap + Math.Max(lcap, titled && cells.Count == 0 ? scap : lcap) + top;
        float h = body + (bar ? barH : 0);
        p.Need(w, h);
        p.Panel();

        Span<RectangleF> stack = stackalloc RectangleF[2];
        p.Arrange(v, "session.stripStack", new RectangleF(0, 0, w, h), false, 0, StripStackParts, stackalloc float[] { body, bar ? barH : -1 }, stack);
        float y0 = stack[0].Y;
        Span<RectangleF> parts = stackalloc RectangleF[2];
        p.Arrange(v, "session.strip", new RectangleF(pad, y0, w - pad * 2, body), true, side * 2, StripParts,
            stackalloc float[] { cells.Count > 0 ? fieldsW : -1, titled ? place : -1 }, parts);
        bool fieldsFirst = !titled || cells.Count == 0 || parts[0].X <= parts[1].X;
        if (cells.Count > 0 && titled) p.Divider(Math.Min(parts[0].Right, parts[1].Right) + side, y0, y0 + body);

        float x = parts[0].X, baseline = y0 + top + vcap;
        for (int i = 0; i < cells.Count; i++)
        {
            var (id, label, value) = cells[i];
            float cw = Column(p, i, vf, HeroPx);
            if (i > 0)
            {
                p.Divider(x + side, y0, y0 + body);
                x += side * 2;
            }
            float lead = i > 0 || !fieldsFirst ? side : pad;
            if (i == 0) p.Display(value, HeroPx, p.P.Heading, new RectangleF(x, y0 + top, cw, vcap), HAlign.Left, VAlign.Bottom);
            else Ink(p, value, vf, p.P.Heading, x, baseline - Cap(vf), cw, HAlign.Left, false);
            Label(p, label, x, baseline + labelGap, cw);
            p.Item("fields", id, new RectangleF(x - lead, y0, cw + lead + side, body));
            x += cw;
        }
        if (titled)
        {
            var titleAlign = fieldsFirst ? HAlign.Right : HAlign.Left;
            Title(p, title, parts[1].X, baseline - lcap, place, titleAlign);
            Ink(p, track, p.Small, p.P.Muted, parts[1].X, baseline + labelGap, place, titleAlign);
        }
        if (bar)
        {
            bool barTop = stack[1].Y < stack[0].Y;
            var clip = p.ClipEnds(p.Bounds, p.Look.Corner, barTop, !barTop);
            Progress(p, stack[1], done);
            p.Unclip(clip);
        }
    }

    private float Column(HudPainter p, int i, Font vf, float heroPx)
    {
        var (id, label, value) = _cells[i];
        return Math.Max(LabelWidth(p, label), i == 0 ? DisplayWidth(p, id, value, heroPx) : ValueWidth(p, id, value, vf));
    }

    private void DrawGrid(HudPainter p, HudView v, string title, string track, bool bar, double? done)
    {
        float d = p.Density, pad = 12 * d, side = 14 * d, cellPad = 9 * d, labelGap = 6 * d, barH = 3 * d;
        var lf = p.Label;
        var vf = p.Font(18, true);
        var hf = p.Font(38, true);
        var cells = _cells;
        bool titled = p.TitleRowShown;
        float top = titled ? p.PillHeight + 1 : 0;
        float lcap = Cap(lf), vcap = Cap(vf), hcap = Cap(hf);
        int others = Math.Max(0, cells.Count - 1);
        int cols = Math.Min(3, others), rows = cols == 0 ? 0 : (others + cols - 1) / cols;
        float cellH = cellPad * 2 + lcap + labelGap + vcap;
        float heroInk = lcap + labelGap + hcap;
        float gridH = cells.Count == 0 ? 0 : Math.Max(rows * cellH, heroInk + cellPad * 2);

        float hw = cells.Count > 0 ? Math.Max(LabelWidth(p, cells[0].Label), ValueWidth(p, cells[0].Id, cells[0].Value, hf)) + pad + side : 0;
        float cw = 0;
        for (int i = 1; i < cells.Count; i++) cw = Math.Max(cw, Math.Max(LabelWidth(p, cells[i].Label), ValueWidth(p, cells[i].Id, cells[i].Value, vf)));
        cw += side * 2;
        float w = hw + cols * cw;
        if (titled) w = Math.Max(w, 8 * d + TitleWidth(p, title) + 16 * d + Math.Min(p.Measure(track, p.Small), 160 * d) + 8 * d);
        if (cells.Count > 0) hw = w - cols * cw;
        w = MathF.Ceiling(w);
        float h = top + gridH + (bar ? pad + barH + pad : pad);
        p.Need(w, h);
        p.Header(title, track);

        float barBlock = pad + barH + pad;
        Span<RectangleF> parts = stackalloc RectangleF[2];
        p.Arrange(v, "session.grid", new RectangleF(0, top, w, gridH + (bar ? barBlock : 0)), false, 0, GridParts,
            stackalloc float[] { gridH > 0 ? gridH : -1, bar ? barBlock : -1 }, parts);
        float gridTop = gridH > 0 ? parts[0].Y : top;
        if (cells.Count > 0)
        {
            var (id, label, value) = cells[0];
            float capTop = gridTop + (gridH - heroInk) / 2;
            Label(p, label, pad, capTop, hw - pad - side);
            Ink(p, value, hf, p.P.Heading, pad, capTop + lcap + labelGap, hw - pad - side, HAlign.Left, false);
            p.Item("fields", id, new RectangleF(0, gridTop, hw, gridH));
        }
        for (int k = 0; k < others; k++)
        {
            var (id, label, value) = cells[k + 1];
            int r = k / cols, c = k % cols;
            float x0 = hw + c * cw, y0 = gridTop + r * cellH;
            p.Divider(x0, y0 + (r == 0 ? cellPad / 2 : 0), y0 + cellH - (r == rows - 1 ? cellPad / 2 : 0));
            if (r > 0) p.Line(x0, y0, x0 + cw - (c == cols - 1 ? 1 : 0), y0, Rule(p));
            Label(p, label, x0 + side, y0 + cellPad, cw - side * 2);
            Ink(p, value, vf, p.P.Heading, x0 + side, y0 + cellPad + lcap + labelGap, cw - side * 2, HAlign.Left, false);
            p.Item("fields", id, new RectangleF(x0, y0, cw, cellH));
        }
        if (bar) Progress(p, new RectangleF(pad, parts[1].Y + pad, w - pad * 2, barH), done);
    }

    private static readonly string[] PositionParts = { "position", "details" };

    private void DrawPosition(HudPainter p, HudView v, string title, string track, bool bar, double? done)
    {
        const float PosPx = 54;
        float d = p.Density, pad = 12 * d, gap = 10 * d, split = 16 * d, barH = 3 * d, miniGap = 14 * d;
        var lf = p.Label;
        var bf = p.Body;
        var bb = p.Bold;
        var clock = p.Font(26, true);
        var mf = p.Font(15, true);
        bool titled = p.TitleRowShown;
        float top = titled ? p.PillHeight + 1 : 0;
        float lcap = Cap(lf), ccap = Cap(clock), mcap = Cap(mf), bcap = Cap(bf), pcap = DisplayCap(p, PosPx);

        var f = v.Frame;
        int me = f.PlayerCarIdx;
        string overall = (uint)me < TelemetryFrame.MaxCars && f.CarPosition[me] > 0 ? RaceFormat.Pos(f.CarPosition[me]) : RaceFormat.Missing;
        bool multiclass = f.Session.NumCarClasses > 1;
        string big = multiclass && v.Race.StandingOf(me)?.ClassPosition is > 0 and var cp ? RaceFormat.Pos(cp) : overall;
        string subLead = multiclass ? PooledText.Concat(f.Session.Player?.ClassLabel is { Length: > 0 } cn ? cn : "Class", " · ") : "";
        string subBold = multiclass ? overall : v.Race.LastStandingsCount > 0 ? PooledText.Number("of ", v.Race.LastStandingsCount, "") : "";
        string subTail = multiclass ? " overall" : "";

        var rest = _rest;
        rest.Clear();
        int hero = -1, second = -1;
        for (int i = 0; i < _cells.Count; i++)
        {
            if (_cells[i].Id is "pos" or "cpos") continue;
            if (hero < 0) hero = i;
            else if (second < 0) second = i;
            else rest.Add(i);
        }

        float leftW = Math.Max(DisplayWidth(p, "pos", big, PosPx), p.Measure(subLead, bf) + p.Measure(subBold, bb) + p.Measure(subTail, bf));
        float heroValW = hero >= 0 ? ValueWidth(p, _cells[hero].Id, _cells[hero].Value, clock) : 0;
        float line1 = hero >= 0 ? heroValW + 8 * d + LabelWidth(p, _cells[hero].Label) : 0;
        if (second >= 0) line1 += (line1 > 0 ? 18 * d : 0) + ValueWidth(p, _cells[second].Id, _cells[second].Value, mf) + 6 * d + LabelWidth(p, _cells[second].Label);
        int miniCols = Math.Min(3, rest.Count), miniRows = miniCols == 0 ? 0 : (rest.Count + miniCols - 1) / miniCols;
        Array.Clear(_miniWidths);
        for (int k = 0; k < rest.Count; k++)
        {
            var (id, label, value) = _cells[rest[k]];
            _miniWidths[k % miniCols] = Math.Max(_miniWidths[k % miniCols], Math.Max(LabelWidth(p, label), ValueWidth(p, id, value, mf)));
        }
        float miniW = 0;
        for (int c = 0; c < miniCols; c++) miniW += _miniWidths[c] + (c > 0 ? miniGap : 0);
        float rightW = Math.Max(Math.Max(line1, miniW), 150 * d);
        float w = pad + leftW + split * 2 + rightW + pad;
        if (titled) w = Math.Max(w, 8 * d + TitleWidth(p, title) + 16 * d + Math.Min(p.Measure(track, p.Small), 200 * d) + 8 * d);
        rightW = w - pad - leftW - split * 2 - pad;
        w = MathF.Ceiling(w);

        float miniRowH = mcap + 6 * d + lcap;
        float minisH = miniRows > 0 ? miniRows * miniRowH + (miniRows - 1) * gap : 0;
        float rightH = (line1 > 0 ? ccap : 0);
        if (bar) rightH += (rightH > 0 ? gap : 0) + barH;
        if (miniRows > 0) rightH += (rightH > 0 ? gap + 2 * d : 0) + minisH;
        float leftH = pcap + 9 * d + bcap;
        float bodyH = Math.Max(leftH, rightH);
        float bodyTop = top + gap + 2 * d;
        float h = bodyTop + bodyH + pad + 2 * d;
        p.Need(w, h);
        p.Header(title, track);

        Span<RectangleF> boxes = stackalloc RectangleF[2];
        p.Arrange(v, "session.position", new RectangleF(pad, bodyTop, leftW + split * 2 + rightW, bodyH), true, split * 2, PositionParts, stackalloc float[] { leftW, rightW }, boxes);
        float lx = boxes[0].X;
        float ly = bodyTop + (bodyH - leftH) / 2;
        p.Display(big, PosPx, p.P.Heading, new RectangleF(lx, ly, leftW, pcap), HAlign.Left, VAlign.Bottom);
        float sx = lx, sy = ly + pcap + 9 * d;
        if (subLead.Length > 0) { Ink(p, subLead, bf, p.P.Muted, sx, sy, leftW, HAlign.Left, false); sx += p.Measure(subLead, bf); }
        if (subBold.Length > 0) { Ink(p, subBold, bb, p.P.Heading, sx, sy, leftW, HAlign.Left, false); sx += p.Measure(subBold, bb); }
        if (subTail.Length > 0) Ink(p, subTail, bf, p.P.Muted, sx, sy, Math.Max(0, lx + leftW - sx), HAlign.Left, false);
        p.Divider(Math.Min(boxes[0].Right, boxes[1].Right) + split, bodyTop, bodyTop + bodyH);

        float rx = boxes[1].X, ry = bodyTop + (bodyH - rightH) / 2;
        Span<RectangleF> rightParts = stackalloc RectangleF[3];
        p.Arrange(v, "session.positionRight", new RectangleF(rx, ry, rightW, rightH), false, gap, PositionRightParts,
            stackalloc float[] { line1 > 0 ? ccap : -1, bar ? barH : -1, miniRows > 0 ? minisH + (line1 > 0 || bar ? 2 * d : 0) : -1 }, rightParts);
        if (line1 > 0)
        {
            ry = rightParts[0].Y;
            if (hero >= 0)
            {
                var (id, label, value) = _cells[hero];
                float shown = p.Measure(value, clock);
                Ink(p, value, clock, p.P.Heading, rx, ry, shown + 2, HAlign.Left, false);
                Label(p, label, rx + shown + 8 * d, ry + ccap - lcap, LabelWidth(p, label) + 1);
                p.Item("fields", id, new RectangleF(rx, ry, shown + 8 * d + LabelWidth(p, label), ccap));
            }
            if (second >= 0)
            {
                var (id, label, value) = _cells[second];
                float lw = LabelWidth(p, label), vw = p.Measure(value, mf);
                float right = rx + rightW;
                Label(p, label, right - lw - 1, ry + ccap - lcap, lw + 1);
                Ink(p, value, mf, p.P.Heading, right - lw - 6 * d - vw - 1, ry + ccap - mcap, vw + 1, HAlign.Left, false);
                p.Item("fields", id, new RectangleF(right - lw - 6 * d - vw, ry, lw + 6 * d + vw, ccap));
            }
        }
        if (bar) Progress(p, rightParts[1], done);
        if (miniRows > 0)
        {
            ry = rightParts[2].Bottom - minisH;
            for (int k = 0; k < rest.Count; k++)
            {
                var (id, label, value) = _cells[rest[k]];
                int r = k / miniCols, c = k % miniCols;
                float x0 = rx;
                for (int j = 0; j < c; j++) x0 += _miniWidths[j] + miniGap;
                float y0 = ry + r * (miniRowH + gap);
                Ink(p, value, mf, p.P.Heading, x0, y0, _miniWidths[c] + 1, HAlign.Left, false);
                Label(p, label, x0, y0 + mcap + 6 * d, _miniWidths[c] + 1);
                p.Item("fields", id, new RectangleF(x0, y0, _miniWidths[c], miniRowH));
            }
        }
    }
}
