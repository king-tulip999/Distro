using System.Drawing;
using System.Globalization;
using Core.SimRacing.Hud.Editors;
using Core.SimRacing.Race;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Hud.Widgets;

internal sealed class TickerStandingsWidget : HudWidget
{
    public const string Ticker = "ticker", Around = "around", Classes = "classes";
    public const double W = 900, H = 40;
    private const int MaxClasses = 8;
    private const double Even = 0.02;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string[] TickerParts = { "lap", "cars" }, LineParts = { "class", "cars" };

    private static readonly int[] AroundOrder = { 0, 1, 2, 3, 4 };

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("style", "Style", Ticker,
            new HudChoice(Ticker, "Ticker", "The field left to right with gaps to the leader"),
            new HudChoice(Around, "Around you", "Two places each side of you with the gap and whether it's closing"),
            new HudChoice(Classes, "Class lines", "One line per class, yours always including you")),
        HudOption.Bool(TitleRow.Key, TitleRow.Label, false),
        HudOption.Int("perClass", "Cars per class", 5, 2, 12),
        HudClassFilter.Scope,
        HudClassFilter.List,
    };

    private readonly int[] _rows = new int[TelemetryFrame.MaxCars];
    private readonly float[] _widths = new float[TelemetryFrame.MaxCars];
    private readonly bool[] _shown = new bool[TelemetryFrame.MaxCars];
    private readonly int[] _classIds = new int[MaxClasses];
    private readonly PointF[] _tri = new PointF[3];

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Horizontal standings")) return;
        RectangleF area;
        if (p.TitleRowShown) area = p.Header("Standings");
        else
        {
            p.Panel();
            area = p.Bounds.Inflated(-4 * p.Density, -4 * p.Density);
        }
        var standings = v.Race.Standings;
        if (standings.Count == 0)
        {
            p.Text("Standings show once cars are on track", p.Small, p.P.Faint, area, HAlign.Center, VAlign.Center);
            return;
        }
        switch (v.Options.Choice("style"))
        {
            case Around: DrawAround(p, v, area, standings); break;
            case Classes: DrawClasses(p, v, area, standings); break;
            default: DrawTicker(p, v, area, standings); break;
        }
    }

    private static bool ByClass(HudView v) => v.Options.Choice(HudClassFilter.ScopeKey) != HudClassFilter.All;

    private static string Number(TelemetryFrame f, int car) =>
        f.Session.ByCar.TryGetValue(car, out var d) && d.CarNumber.Length > 0 ? PooledText.Concat("#", d.CarNumber) : RaceFormat.Missing;

    private static Color ClassColor(HudPainter p, TelemetryFrame f, int car) => p.ClassColor(f.Session, FieldOrder.ClassOf(f, car));

    private Font ItemFont(HudPainter p, float h) => p.Fitting("0", 999, h - 6 * p.Density, p.Body, p.Small, p.Tiny);

    private static bool ClassGap(HudView v, bool byClass) => byClass || v.Frame.Session.NumCarClasses > 1;

    private string GapText(HudView v, StandingRow r, bool byClass)
    {
        double? gap = byClass ? r.GapToClassLeader : r.GapToLeader;
        int laps = byClass ? r.ClassLapsDown : r.LapsDown;
        bool leader = (byClass ? r.ClassPosition : r.Position) == 1;
        if (leader) return "Leader";
        if (laps > 0 && v.Frame.SessionKind == SessionKind.Race) return PooledText.Number("+", laps, "L");
        return gap is { } g ? RaceFormat.Plus(g, 1) : RaceFormat.Missing;
    }

    private static int Pos(StandingRow r, bool byClass) => byClass ? r.ClassPosition : r.Position;

    private int Filter(HudView v, IReadOnlyList<StandingRow> standings)
    {
        int n = 0;
        for (int i = 0; i < standings.Count; i++)
            if (v.ClassShown(standings[i].CarIdx)) _rows[n++] = i;
        return n;
    }

    private void DrawTicker(HudPainter p, HudView v, RectangleF area, IReadOnlyList<StandingRow> standings)
    {
        float d = p.Density, gap = 3 * d, pad = 8 * d, bar = 3 * d;
        var f = v.Frame;
        var font = ItemFont(p, area.Height);
        var bold = p.BoldOf(font);
        bool byClass = ByClass(v);
        int n = Filter(v, standings);
        p.Need(p.Bounds.Width, p.LineHeight(font) + 6 * d + p.Bounds.Height - area.Height);

        int fastestCar = FieldOrder.FastestLap(f, byClass ? f.Session.Player?.ClassId : null)?.Car ?? -1;
        int leaderRow = n > 0 ? standings[_rows[0]].CarIdx : -1;
        int leaderLap = leaderRow >= 0 ? Math.Max(f.CarLap[leaderRow], f.CarLapCompleted[leaderRow]) : 0;

        string lap = leaderLap > 0 ? PooledText.Number("L", leaderLap) : PooledText.Concat("L", RaceFormat.Missing);
        string of = f.CurrentSession?.Laps is > 0 and var total ? PooledText.Number("/ ", total) : Strips.TimeLeft(v) ?? "";
        float lapW = p.Measure(lap, bold) + (of.Length > 0 ? p.Measure(of, font) + 5 * d : 0) + pad * 2;
        Span<RectangleF> boxes = stackalloc RectangleF[2];
        p.Arrange(v, "hstandings.ticker", area, true, gap, TickerParts, stackalloc float[] { lapW, 0 }, boxes);
        var lapBox = boxes[0];
        p.Fill(lapBox, p.P.Accent, p.CornerRadius(4));
        float lx = lapBox.X + pad, lw = p.Measure(lap, bold);
        p.Text(lap, bold, p.P.OnAccent, new RectangleF(lx, area.Y, lw + 2, area.Height), HAlign.Left, VAlign.Center, false);
        if (of.Length > 0) p.Text(of, font, HudPalette.Alpha(p.P.OnAccent, 210), new RectangleF(lx + lw + 5 * d, area.Y, p.Measure(of, font) + 2, area.Height), HAlign.Left, VAlign.Center, false);

        float x0 = boxes[1].X, room = boxes[1].Right - x0;
        int me = -1;
        float all = 0;
        for (int k = 0; k < n; k++)
        {
            var r = standings[_rows[k]];
            if (r.CarIdx == f.PlayerCarIdx) me = k;
            _widths[k] = ItemWidth(p, v, r, font, bold, byClass, pad, bar);
            _shown[k] = false;
            all += _widths[k] + (k > 0 ? gap : 0);
        }
        if (all <= room)
        {
            for (int k = 0; k < n; k++) _shown[k] = true;
        }
        else if (n > 0) Window(n, me, room, gap, 9 * d);
        float x = x0;
        for (int k = 0; k < n; k++)
        {
            if (!_shown[k]) continue;
            if (k > 0 && !_shown[k - 1])
            {
                float cx = x + 2 * d;
                for (int dot = 0; dot < 3; dot++) p.Dot(cx + dot * 3 * d, area.Y + area.Height / 2, 1 * d, p.P.Faint);
                x += 9 * d;
            }
            var r = standings[_rows[k]];
            DrawItem(p, v, r, new RectangleF(x, area.Y, _widths[k], area.Height), font, bold, byClass, pad, bar, r.CarIdx == fastestCar);
            x += _widths[k] + gap;
        }
    }

    private void Window(int n, int me, float room, float gap, float split)
    {
        float used = _widths[0];
        int centre = Math.Max(1, me);
        if (centre >= n || used > room) { if (used <= room) _shown[0] = true; return; }
        float first = _widths[centre] + gap + (centre > 1 ? split : 0);
        if (used + first > room)
        {
            if (me > 0 && _widths[me] <= room) _shown[me] = true;
            else _shown[0] = true;
            return;
        }
        _shown[0] = true;
        used += first;
        _shown[centre] = true;
        int lo = centre, hi = centre;
        bool grew = true;
        while (grew)
        {
            grew = false;
            if (hi + 1 < n && used + _widths[hi + 1] + gap <= room)
            {
                used += _widths[++hi] + gap;
                _shown[hi] = true;
                grew = true;
            }
            if (lo > 1)
            {
                float extra = _widths[lo - 1] + gap - (lo - 1 == 1 ? split : 0);
                if (used + extra <= room)
                {
                    used += extra;
                    _shown[--lo] = true;
                    grew = true;
                }
            }
        }
    }

    private float ItemWidth(HudPainter p, HudView v, StandingRow r, Font font, Font bold, bool byClass, float pad, float bar)
    {
        var f = v.Frame;
        float w = pad * 2 + p.Measure(PooledText.Number("", Math.Max(0, Pos(r, byClass))), bold) + 7 * p.Density + bar + 5 * p.Density + p.Measure(Number(f, r.CarIdx), font) + 7 * p.Density;
        float tail = f.CarOnPitRoad[r.CarIdx] ? p.Measure("PIT", p.Label) + 10 * p.Density : p.Measure(GapText(v, r, ClassGap(v, byClass)), font);
        return MathF.Ceiling(w + tail);
    }

    private void DrawItem(HudPainter p, HudView v, StandingRow r, RectangleF box, Font font, Font bold, bool byClass, float pad, float bar, bool fastest)
    {
        var f = v.Frame;
        float d = p.Density;
        bool me = r.CarIdx == f.PlayerCarIdx;
        if (me) p.Fill(box, p.Solid(p.P.Player, 230), p.CornerRadius(4));
        else p.Tile(box);
        var ink = me ? p.P.PlayerText : p.P.Heading;
        float x = box.X + pad;
        string pos = PooledText.Number("", Math.Max(0, Pos(r, byClass)));
        float pw = p.Measure(pos, bold);
        p.Text(pos, bold, ink, new RectangleF(x, box.Y, pw + 2, box.Height), HAlign.Left, VAlign.Center, false);
        x += pw + 7 * d;
        float lh = p.LineHeight(font);
        p.Fill(new RectangleF(x, box.Y + (box.Height - lh) / 2 + 1, bar, lh - 2), ClassColor(p, f, r.CarIdx));
        x += bar + 5 * d;
        string num = Number(f, r.CarIdx);
        float nw = p.Measure(num, font);
        p.Text(num, font, me ? p.P.PlayerText : p.P.Text, new RectangleF(x, box.Y, nw + 2, box.Height), HAlign.Left, VAlign.Center, false);
        x += nw + 7 * d;
        if (f.CarOnPitRoad[r.CarIdx])
        {
            float cw = p.Measure("PIT", p.Label) + 8 * d, ch = p.LineHeight(p.Label) + 2 * d;
            p.Pill(new RectangleF(x, box.Y + (box.Height - ch) / 2, cw, ch), "PIT", p.P.WarnSoft, p.Label, p.P.WarnText);
            return;
        }
        string gap = GapText(v, r, ClassGap(v, byClass));
        var c = me ? p.P.PlayerText : fastest ? p.P.Fastest : p.P.Muted;
        p.Text(gap, font, c, new RectangleF(x, box.Y, box.Right - x, box.Height), HAlign.Left, VAlign.Center, false);
    }

    private void Triangle(HudPainter p, float x, float cy, float side, bool down, Color c)
    {
        float h = side * 0.8f;
        _tri[0] = new PointF(x, down ? cy - h / 2 : cy + h / 2);
        _tri[1] = new PointF(x + side, down ? cy - h / 2 : cy + h / 2);
        _tri[2] = new PointF(x + side / 2, down ? cy + h / 2 : cy - h / 2);
        p.G.FillPolygon(p.Brush(c), _tri);
    }

    private void DrawAround(HudPainter p, HudView v, RectangleF area, IReadOnlyList<StandingRow> standings)
    {
        float d = p.Density, gap = 3 * d;
        var f = v.Frame;
        int meCar = f.PlayerCarIdx;
        var myRow = v.Race.StandingOf(meCar);
        bool multi = f.Session.NumCarClasses > 1;
        int myClass = FieldOrder.ClassOf(f, meCar);
        int n = 0, me = -1;
        for (int i = 0; i < standings.Count; i++)
        {
            if (multi && FieldOrder.ClassOf(f, standings[i].CarIdx) != myClass) continue;
            if (standings[i].CarIdx == meCar) me = n;
            _rows[n++] = i;
        }
        p.Need(p.Bounds.Width, p.LineHeight(p.SmallBold) + 6 * d + p.Bounds.Height - area.Height);
        if (me < 0 || myRow is not { } mine)
        {
            p.Text("Shows once you're in the standings", p.Small, p.P.Faint, area, HAlign.Center, VAlign.Center);
            return;
        }
        float youW = Math.Min(110 * d, area.Width * 0.16f), w = (area.Width - youW - gap * 4) / 4;
        double? lapTime = FieldOrder.LapTimeOf(f, meCar);
        Span<RectangleF> boxes = stackalloc RectangleF[5];
        HudArrange.Lay(area, true, gap, AroundOrder, stackalloc float[] { w, w, youW, w, w }, boxes);
        for (int slot = -2; slot <= 2; slot++)
        {
            var box = boxes[slot + 2];
            int k = me + slot;
            if (slot == 0) { DrawYou(p, v, box, mine, multi); continue; }
            if (k < 0 || k >= n) { p.Fill(box, HudPalette.Alpha(p.P.LineStrong, 50), p.CornerRadius(4)); continue; }
            DrawNeighbour(p, v, box, standings[_rows[k]], mine, slot, multi, lapTime);
        }
    }

    private void DrawYou(HudPainter p, HudView v, RectangleF box, StandingRow mine, bool multi)
    {
        float d = p.Density;
        p.Fill(box, p.Solid(p.P.Player, 235), p.CornerRadius(4));
        int pos = multi ? mine.ClassPosition : mine.Position;
        string tag = RaceFormat.Pos(pos);
        bool two = box.Height >= p.LineHeight(p.Label) + p.LineHeight(p.Bold) + 2 * d;
        if (two)
        {
            float lh = p.LineHeight(p.Label), bh = p.LineHeight(p.Bold), top = box.Y + (box.Height - lh - bh) / 2;
            p.Text(tag, p.Label, HudPalette.Alpha(p.P.PlayerText, 220), new RectangleF(box.X, top, box.Width, lh), HAlign.Center, VAlign.Center, false);
            p.Text("You", p.Bold, p.P.PlayerText, new RectangleF(box.X, top + lh, box.Width, bh), HAlign.Center, VAlign.Center, false);
        }
        else
        {
            string both = PooledText.Concat(tag, "  You");
            p.Text(p.Measure(both, p.SmallBold) + 8 * d <= box.Width ? both : "You", p.SmallBold, p.P.PlayerText, box, HAlign.Center, VAlign.Center);
        }
    }

    private void DrawNeighbour(HudPainter p, HudView v, RectangleF box, StandingRow r, StandingRow mine, int slot, bool multi, double? lapTime)
    {
        float d = p.Density;
        var f = v.Frame;
        bool ahead = slot < 0, near = Math.Abs(slot) == 1;
        var role = ahead ? p.P.Attack : p.P.Defend;
        if (near)
        {
            p.Fill(box, p.Solid(HudPalette.Mix(p.PanelColor, role, 0.2), 230), p.CornerRadius(4));
            p.Fill(new RectangleF(box.X, box.Y, box.Width, 2 * d), role);
        }
        else p.Tile(box);
        var inner = box.Inflated(-7 * d, 0);
        int pos = multi ? r.ClassPosition : r.Position;
        double? gap = (multi ? r.GapToClassLeader : r.GapToLeader) is { } g && (multi ? mine.GapToClassLeader : mine.GapToLeader) is { } m ? m - g : null;
        int lapsApart = (multi ? mine.ClassLapsDown : mine.LapsDown) - (multi ? r.ClassLapsDown : r.LapsDown);
        string gapText = lapsApart != 0 && f.SessionKind == SessionKind.Race ? PooledText.Number(lapsApart > 0 ? "+" : "−", Math.Abs(lapsApart), "L") : RaceFormat.Gap(gap, 1);
        double? perLap = v.Race.Gaps.Trend(r.CarIdx) is { } t && lapTime is { } lt ? t * lt : null;
        string name = f.Session.ByCar.TryGetValue(r.CarIdx, out var dr) ? RaceFormat.Name(dr.Name.Length > 0 ? dr.Name : dr.TeamName, "initial") : RaceFormat.Missing;
        var ink = near ? role : p.P.Text;
        string tag = RaceFormat.Pos(pos);

        float lh = p.LineHeight(p.Label), nh = p.LineHeight(p.SmallBold);
        bool two = box.Height >= lh + nh - 2 * d;
        float tri = MathF.Round(6 * d);
        string rate = perLap is { } pl && Math.Abs(pl) >= Even ? PooledText.Number("", Math.Abs(pl), "0.0", Inv, "/lap") : "";
        float rateW = rate.Length > 0 ? p.Measure(rate, p.Small) + tri + 3 * d : 0;
        float gapW = p.Measure(gapText, p.SmallBold);
        float top = two ? box.Y + (box.Height - lh - nh) / 2 : box.Y;
        float lineH = two ? lh : box.Height;
        string label = two ? p.LabelText(PooledText.Concat(tag, " · ", Number(f, r.CarIdx))) : p.LabelText(tag);
        float tagW = p.Measure(label, p.Label) + 2;
        if (!two && tagW + gapW + 12 * d > inner.Width) { label = ""; tagW = -5 * d; }
        if (label.Length > 0) p.Text(label, p.Label, near ? role : p.P.Muted, new RectangleF(inner.X, top, Math.Min(tagW, inner.Width), lineH), HAlign.Left, VAlign.Center);
        float nameX = two ? inner.X : inner.X + tagW + 5 * d;
        float nameY = two ? top + lh : box.Y, nameH = two ? nh : box.Height;
        float right = inner.Right;
        bool rateDrawn = false;
        p.Text(gapText, p.SmallBold, ink, new RectangleF(right - gapW - 1, nameY, gapW + 1, nameH), HAlign.Right, VAlign.Center, false);
        if (rate.Length > 0)
        {
            float rateRight = two ? right : right - gapW - 8 * d;
            float rateLeft = rateRight - rateW;
            float rateY = two ? top : box.Y, rateH = two ? lh : box.Height;
            if (rateLeft >= (two ? inner.X + tagW + 6 * d : nameX + 50 * d))
            {
                bool closing = perLap < 0;
                var c = closing == ahead ? p.P.GoodText : p.P.DangerText;
                Triangle(p, rateLeft, rateY + rateH / 2, tri, closing, c);
                p.Text(rate, p.Small, c, new RectangleF(rateLeft + tri + 3 * d, rateY, rateW - tri - 3 * d + 1, rateH), HAlign.Left, VAlign.Center, false);
                rateDrawn = true;
            }
        }
        float nameRight = right - gapW - 6 * d - (rateDrawn && !two ? rateW + 8 * d : 0);
        if (nameRight - nameX >= 28 * d) p.Text(name, p.SmallBold, p.P.Heading, new RectangleF(nameX, nameY, nameRight - nameX, nameH), HAlign.Left, VAlign.Center);
    }

    private void DrawClasses(HudPainter p, HudView v, RectangleF area, IReadOnlyList<StandingRow> standings)
    {
        float d = p.Density, gap = 3 * d;
        var f = v.Frame;
        int count = 0;
        for (int i = 0; i < standings.Count && count < MaxClasses; i++)
        {
            int cls = FieldOrder.ClassOf(f, standings[i].CarIdx);
            if (!v.ClassShown(standings[i].CarIdx) || Array.IndexOf(_classIds, cls, 0, count) >= 0) continue;
            _classIds[count++] = cls;
        }
        var font = p.Small;
        var bold = p.SmallBold;
        float lineH = p.LineHeight(bold) + 6 * d;
        p.Need(p.Bounds.Width, count * lineH + (count - 1) * gap + p.Bounds.Height - area.Height);
        if (count == 0) return;
        int myClass = FieldOrder.ClassOf(f, f.PlayerCarIdx);
        int lines = Math.Clamp((int)((area.Height + gap) / (lineH + gap)), 1, count);
        if (lines < count)
        {
            int mine = Array.IndexOf(_classIds, myClass, 0, count);
            if (mine >= lines) _classIds[lines - 1] = myClass;
        }
        float h = Math.Min(lineH + 6 * d, (area.Height - gap * (lines - 1)) / lines);
        float top = area.Y + (area.Height - h * lines - gap * (lines - 1)) / 2;
        float pillW = 0;
        for (int k = 0; k < lines; k++) pillW = Math.Max(pillW, p.Measure(ClassLabel(f, _classIds[k]), p.Label));
        pillW += 12 * d;
        for (int k = 0; k < lines; k++)
            DrawClassLine(p, v, new RectangleF(area.X, top + k * (h + gap), area.Width, h), _classIds[k], pillW, standings, font, bold);
    }

    private static string ClassLabel(TelemetryFrame f, int classId)
    {
        var drivers = f.Session.Drivers;
        for (int i = 0; i < drivers.Count; i++)
            if (drivers[i].ClassId == classId && drivers[i].Races) return drivers[i].ClassLabel;
        return "";
    }

    private void DrawClassLine(HudPainter p, HudView v, RectangleF line, int classId, float pillW, IReadOnlyList<StandingRow> standings, Font font, Font bold)
    {
        float d = p.Density, gap = 3 * d, pad = 7 * d;
        var f = v.Frame;
        var color = p.ClassColor(f.Session, classId);
        Span<RectangleF> boxes = stackalloc RectangleF[2];
        p.Arrange(v, "hstandings.classes", line, true, gap, LineParts, stackalloc float[] { pillW, 0 }, boxes);
        p.Pill(boxes[0], ClassLabel(f, classId), color, p.Label);
        float x0 = boxes[1].X, room = boxes[1].Right - x0;
        int n = 0, me = -1;
        for (int i = 0; i < standings.Count; i++)
        {
            if (FieldOrder.ClassOf(f, standings[i].CarIdx) != classId) continue;
            if (standings[i].CarIdx == f.PlayerCarIdx) me = n;
            float w = pad * 2 + p.Measure(PooledText.Number("", Math.Max(0, standings[i].ClassPosition)), bold) + 5 * d + p.Measure(Number(f, standings[i].CarIdx), font);
            _widths[n] = MathF.Ceiling(w);
            _rows[n++] = i;
        }
        string moreSample = "+00 more";
        float moreW = p.Measure(moreSample, font) + 6 * d;
        int shown = 0;
        float used = 0;
        while (shown < n && used + _widths[shown] + (shown > 0 ? gap : 0) <= room - moreW) { used += _widths[shown] + (shown > 0 ? gap : 0); shown++; }
        if (shown == n)
        {
            shown = 0; used = 0;
            while (shown < n && used + _widths[shown] + (shown > 0 ? gap : 0) <= room) { used += _widths[shown] + (shown > 0 ? gap : 0); shown++; }
        }
        shown = Math.Min(shown, v.Options.Int("perClass"));
        bool swap = me >= shown && shown > 0;
        if (swap)
            while (shown > 1 && Sum(shown - 1, gap) + gap + _widths[me] > room - moreW) shown--;
        float x = x0;
        for (int s = 0; s < shown; s++)
        {
            int k = swap && s == shown - 1 ? me : s;
            var r = standings[_rows[k]];
            bool isMe = k == me;
            var box = new RectangleF(x, line.Y, _widths[k], line.Height);
            if (isMe) p.Fill(box, p.Solid(p.P.Player, 230), p.CornerRadius(4));
            else p.Tile(box);
            string pos = PooledText.Number("", Math.Max(0, r.ClassPosition));
            float pw = p.Measure(pos, bold);
            p.Text(pos, bold, isMe ? p.P.PlayerText : p.P.Heading, new RectangleF(box.X + pad, box.Y, pw + 2, box.Height), HAlign.Left, VAlign.Center, false);
            p.Text(Number(f, r.CarIdx), font, isMe ? p.P.PlayerText : p.P.Text, new RectangleF(box.X + pad + pw + 5 * d, box.Y, box.Right - box.X - pad - pw - 5 * d, box.Height), HAlign.Left, VAlign.Center, false);
            x += _widths[k] + gap;
        }
        int hidden = n - shown;
        if (hidden > 0) p.Text(PooledText.Number("+", hidden, " more"), font, p.P.Faint, new RectangleF(x + 3 * d, line.Y, Math.Max(0, boxes[1].Right - x - 3 * d), line.Height), HAlign.Left, VAlign.Center);
    }

    private float Sum(int count, float gap)
    {
        float s = 0;
        for (int i = 0; i < count; i++) s += _widths[i] + (i > 0 ? gap : 0);
        return s;
    }
}
