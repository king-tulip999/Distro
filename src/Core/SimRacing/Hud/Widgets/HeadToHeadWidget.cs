using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using Units = Core.Services.CapsUnits;
using Core.SimRacing.Race;
using Core.SimRacing.Telemetry;
using Core.SimRacing.TrackMaps;

namespace Core.SimRacing.Hud.Widgets;

internal sealed class HeadToHeadWidget : HudWidget
{
    public const string Rows = "rows", Ticker = "ticker", Corners = "corners", Map = "map", Battle = "battle", Trend = "trend";
    private const double Hysteresis = 0.25, InRange = 1.0, PieceScale = 0.1, Even = 0.02, PieceEven = 0.004;
    private const int MapSamples = 240, SamplesPerPiece = MapSamples / DuelLog.Pieces;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("style", "Style", Rows,
            new HudChoice(Rows, "Duel rows", "Ahead, you and behind, with each sector, last lap, gap and trend"),
            new HudChoice(Ticker, "Ticker", "One line per close car: gap, the sector that swung most, trend"),
            new HudChoice(Corners, "Corner bars", "Time gained and lost through 24 parts of the lap, with turn names"),
            new HudChoice(Map, "Track map", "Time gained and lost painted on the track map"),
            new HudChoice(Battle, "Battle line", "Both gaps on one bar, and where to attack or defend"),
            new HudChoice(Trend, "Gap trend", "The gap at the line each lap, and whether anyone catches before the flag")),
        TitleRow,
        HudOption.Number("within", "Show a car within", 2, 0.5, 10, 0.1, Units.S, "A car shows once the gap to it is this small, and goes again a little after it grows past it"),
        HudOption.Choice("cars", "Cars", "both", new HudChoice("both", "Ahead and behind"), new HudChoice("ahead", "Only the car ahead"), new HudChoice("behind", "Only the car behind")),
        HudOption.Choice("sectors", "Sectors", "all", new HudChoice("all", "Every sector line"), new HudChoice("3", "Merge to 3", "Joins iRacing's sector lines into three parts of the lap")),
    };

    private readonly DuelSnapshot _ahead = new(), _behind = new();
    private readonly MySectors _mine = new();
    private bool _stickyAhead, _stickyBehind, _showAhead, _showBehind;
    private long _seq = long.MinValue;
    private IReadOnlyList<double>? _rawStarts;
    private int _target = -1;
    private int[] _groups = Array.Empty<int>();
    private IReadOnlyList<double> _starts = Array.Empty<double>();
    private readonly PointF[] _tri = new PointF[3];
    private readonly List<(string Text, Font Font, Color Color, bool Space)> _runs = new();
    private readonly HudMapSource _maps = new();

    public override bool HasContent(HudView v)
    {
        Refresh(v, false);
        return _showAhead || _showBehind;
    }

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Head to head")) return;
        Refresh(v, p.IsPreview);
        switch (v.Options.Choice("style"))
        {
            case Ticker: DrawTicker(p, v); break;
            case Corners: DrawCorners(p, v); break;
            case Map: DrawMap(p, v); break;
            case Battle: DrawBattle(p, v); break;
            case Trend: DrawTrend(p, v); break;
            default: DrawRows(p, v); break;
        }
    }

    private void Refresh(HudView v, bool preview)
    {
        var f = v.Frame;
        int target = v.Options.Choice("sectors") == "3" ? 3 : 0;
        var raw = v.Race.Duels.SectorStarts;
        if (!ReferenceEquals(raw, _rawStarts) || target != _target)
        {
            _rawStarts = raw;
            _target = target;
            _groups = DuelSectors.Groups(raw, target);
            _starts = _groups.Select(i => raw[i]).ToArray();
            _seq = long.MinValue;
        }
        if (f.Sequence != _seq)
        {
            _seq = f.Sequence;
            var (a, b) = DuelLog.Rivals(f);
            double now = f.SessionTime ?? 0;
            bool race = f.SessionKind == SessionKind.Race;
            int me = f.PlayerCarIdx;
            var log = v.Race.Duels;
            log.Fill(_ahead, me, a, race, now, a >= 0 && f.CarOnPitRoad[a]);
            log.Fill(_behind, me, b, race, now, b >= 0 && f.CarOnPitRoad[b]);
            log.FillMine(_mine, me, now);
            MergeSectors(_ahead);
            MergeSectors(_behind);
            _mine.Count = DuelSectors.Merge(_mine.Sector, _mine.Count, _groups);
            string cars = v.Options.Choice("cars");
            double within = v.Options.Number("within");
            _stickyAhead = Visible(_ahead, cars != "behind", within, _stickyAhead);
            _stickyBehind = Visible(_behind, cars != "ahead", within, _stickyBehind);
        }
        _showAhead = _stickyAhead;
        _showBehind = _stickyBehind;
        if (preview && !_showAhead && !_showBehind)
        {
            string cars = v.Options.Choice("cars");
            _showAhead = cars != "behind" && _ahead.HasGap;
            _showBehind = cars != "ahead" && _behind.HasGap;
        }
    }

    private void MergeSectors(DuelSnapshot s)
    {
        DuelSectors.Merge(s.LastSector, s.Sectors, _groups);
        s.Sectors = DuelSectors.Merge(s.Sector, s.Sectors, _groups);
    }

    private static bool Visible(DuelSnapshot s, bool allowed, double within, bool was)
    {
        if (!allowed || s.Car < 0 || !s.HasGap) return false;
        double g = Math.Abs(s.Gap);
        return g <= within || was && g <= within + Hysteresis;
    }

    private bool Shown(DuelRole r) => r == DuelRole.Ahead ? _showAhead : _showBehind;

    private DuelSnapshot Of(DuelRole r) => r == DuelRole.Ahead ? _ahead : _behind;

    private int ShownCount => (_showAhead ? 1 : 0) + (_showBehind ? 1 : 0);

    private static readonly DuelRole[] Roles = { DuelRole.Ahead, DuelRole.Behind };

    private static Color RoleColor(HudPainter p, DuelRole r) => r == DuelRole.Ahead ? p.P.Attack : p.P.Defend;

    private static string RoleWord(DuelRole r) => r == DuelRole.Ahead ? "Attack" : "Defend";

    private static string WithinText(HudView v) => PooledText.Number("Within ", v.Options.Number("within"), "0.0", Inv, " " + Units.S);

    private static string Nobody(HudView v) => PooledText.Number("Nobody within ", v.Options.Number("within"), "0.0", Inv, " " + Units.S);

    private static string GapText(DuelRole r, double gap) => PooledText.Number(r == DuelRole.Ahead ? "+" : "−", Math.Abs(gap), "0.000", Inv);

    private static string Number(TelemetryFrame f, int car) =>
        f.Session.ByCar.TryGetValue(car, out var d) && d.CarNumber.Length > 0 ? PooledText.Concat("#", d.CarNumber) : RaceFormat.Missing;

    private static string Position(TelemetryFrame f, int car)
    {
        if ((uint)car >= TelemetryFrame.MaxCars) return RaceFormat.Missing;
        int pos = f.CarClassPosition[car] > 0 ? f.CarClassPosition[car] : f.CarPosition[car];
        return pos > 0 ? PooledText.Number("", pos) : RaceFormat.Missing;
    }

    private static string Seconds(double s) => s >= 60 ? RaceFormat.LapTime(s, 2) : PooledText.Number("", s, "0.00", Inv);

    private static string Delta(double v) => RaceFormat.Gap(v, 2);

    private static Color DeltaColor(HudPainter p, double v) => Math.Abs(v) < Even ? p.P.Muted : v < 0 ? p.P.GoodText : p.P.DangerText;

    private float TriangleSide(HudPainter p) => MathF.Round(6 * p.Density);

    private float TagHeight(HudPainter p) => MathF.Ceiling(p.LineHeight(p.Label) + 2 * p.Density);

    private float TagWidth(HudPainter p, DuelRole r) => MathF.Ceiling(p.Measure(p.LabelText(RoleWord(r)), p.Label) + TriangleSide(p) + 15 * p.Density);

    private float WidestTag(HudPainter p) => Math.Max(TagWidth(p, DuelRole.Ahead), TagWidth(p, DuelRole.Behind));

    private void Triangle(HudPainter p, float x, float cy, float side, bool up, Color c)
    {
        float h = side * 0.8f;
        if (up)
        {
            _tri[0] = new PointF(x, cy + h / 2);
            _tri[1] = new PointF(x + side, cy + h / 2);
            _tri[2] = new PointF(x + side / 2, cy - h / 2);
        }
        else
        {
            _tri[0] = new PointF(x, cy - h / 2);
            _tri[1] = new PointF(x + side, cy - h / 2);
            _tri[2] = new PointF(x + side / 2, cy + h / 2);
        }
        p.G.FillPolygon(p.Brush(c), _tri);
    }

    private void Pointer(HudPainter p, float tipX, float cy, float side, bool left, Color c)
    {
        float h = side * 0.6f, back = left ? tipX + side * 0.8f : tipX - side * 0.8f;
        _tri[0] = new PointF(tipX, cy);
        _tri[1] = new PointF(back, cy - h);
        _tri[2] = new PointF(back, cy + h);
        p.G.FillPolygon(p.Brush(c), _tri);
    }

    private float Tag(HudPainter p, DuelRole r, float x, float cy)
    {
        float d = p.Density, h = TagHeight(p), w = TagWidth(p, r), s = TriangleSide(p);
        var c = RoleColor(p, r);
        var box = new RectangleF(x, MathF.Round(cy - h / 2), w, h);
        p.Fill(box, p.Solid(HudPalette.Mix(p.PanelColor, c, 0.24), 230), p.CornerRadius(3));
        Triangle(p, x + 5 * d, box.Y + h / 2, s, r == DuelRole.Ahead, c);
        float tx = x + 5 * d + s + 4 * d;
        p.Text(p.LabelText(RoleWord(r)), p.Label, c, new RectangleF(tx, box.Y, box.Right - tx, h), HAlign.Left, VAlign.Center, false);
        return box.Right;
    }

    private float ChipWidth(HudPainter p, string text) => MathF.Ceiling(p.Measure(p.LabelText(text), p.Label) + 10 * p.Density);

    private float Chip(HudPainter p, string text, float x, float cy)
    {
        float h = TagHeight(p), w = ChipWidth(p, text);
        var box = new RectangleF(x, MathF.Round(cy - h / 2), w, h);
        p.Fill(box, p.Solid(p.P.WarnSoft, 230), p.CornerRadius(3));
        p.Text(p.LabelText(text), p.Label, p.P.WarnText, box, HAlign.Center, VAlign.Center, false);
        return box.Right;
    }

    private static string? Flag(DuelSnapshot s) => s.InPits ? "Pit" : s.OffTrack ? "Off track" : null;

    private void Cell(HudPainter p, RectangleF r, DuelValue c, bool absolute, double now)
    {
        var tile = p.Solid(p.Raised(p.P.Dark ? 0.1 : 0.07), 220);
        float radius = p.CornerRadius(4);
        if (c.State == DuelState.None)
        {
            p.Fill(r, HudPalette.Alpha(tile, 110), radius);
            p.Text(RaceFormat.Missing, p.SmallBold, p.P.Faint, r, HAlign.Center, VAlign.Center, false);
            return;
        }
        bool ghost = c.State == DuelState.Ghost;
        Color fg = absolute ? p.P.Heading : DeltaColor(p, c.Value), bg = tile;
        if (!absolute && c.State == DuelState.Done && Math.Abs(c.Value) >= Even) bg = p.Solid(c.Value < 0 ? p.P.GoodSoft : p.P.DangerSoft, 220);
        if (ghost)
        {
            bg = HudPalette.Alpha(bg, 100);
            fg = HudPalette.Alpha(fg, 120);
        }
        p.Fill(r, bg, radius);
        if (c.State == DuelState.Live)
        {
            p.Outline(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), p.P.Accent, 1, radius);
            int alpha = p.ReducedMotion ? 255 : (int)(150 + 105 * Math.Abs(Math.Sin(now * Math.PI)));
            float dot = 2 * p.Density;
            p.Dot(r.Right - 4 * p.Density, r.Y + 4 * p.Density, dot, HudPalette.Alpha(p.P.AccentText, alpha));
        }
        p.Text(absolute ? Seconds(c.Value) : Delta(c.Value), p.SmallBold, fg, r, HAlign.Center, VAlign.Center, false);
    }

    private float Space(HudPainter p, Font font) => Math.Max(1, p.Measure("x x", font) - p.Measure("xx", font));

    private void Run(string text, Font font, Color color, bool space = true) => _runs.Add((text, font, color, space));

    private float RunsWidth(HudPainter p)
    {
        float w = 0;
        for (int i = 0; i < _runs.Count; i++)
        {
            var (text, font, _, space) = _runs[i];
            if (i > 0 && space) w += Space(p, font);
            w += p.Measure(text, font);
        }
        return w;
    }

    private void DrawRuns(HudPainter p, RectangleF line)
    {
        float x = line.X;
        for (int i = 0; i < _runs.Count; i++)
        {
            var (text, font, color, space) = _runs[i];
            if (i > 0 && space) x += Space(p, font);
            float w = p.Measure(text, font), room = line.Right - x;
            if (room <= 2) break;
            p.Text(text, font, color, new RectangleF(x, line.Y, Math.Min(w + 2, room), line.Height), HAlign.Left, VAlign.Center, w > room);
            x += w;
        }
        _runs.Clear();
    }

    private void TrendRuns(HudPainter p, DuelSnapshot s, DuelRole r, bool withRange)
    {
        var faint = p.P.Faint;
        if (s.TrendLaps == 0)
        {
            Run("Trend after one full lap", p.Small, faint);
            return;
        }
        bool ahead = r == DuelRole.Ahead;
        double closing = s.Closing(r);
        if (closing > Even) Run(PooledText.Number(ahead ? "Closing " : "Catching you ", closing, "0.00", Inv, " a lap"), p.SmallBold, ahead ? p.P.GoodText : p.P.DangerText);
        else if (closing < -Even) Run(PooledText.Number(ahead ? "Pulling away " : "Dropping back ", -closing, "0.00", Inv, " a lap"), p.SmallBold, ahead ? p.P.DangerText : p.P.GoodText);
        else Run("Steady", p.SmallBold, p.P.Muted);
        if (!withRange) return;
        double gap = Math.Abs(s.Gap);
        if (closing > Even && gap > InRange)
        {
            int n = (int)Math.Ceiling((gap - InRange) / closing);
            Run("·", p.Small, faint);
            Run(PooledText.Concat("within a second in ≈", RaceFormat.Count(n, "lap")), p.Small, faint);
        }
        else if (gap <= InRange)
        {
            Run("·", p.Small, faint);
            Run(ahead ? "in range" : "right behind you", p.Small, faint);
        }
    }

    private void DrawEmpty(HudPainter p, HudView v, string title)
    {
        float h = p.LineHeight(p.Small) * 2.2f;
        p.NeedBelow(p.HeaderBody(), h);
        var area = p.Header(title, null, null, h);
        p.Text(Nobody(v), p.Small, p.P.Faint, area, HAlign.Center, VAlign.Center);
    }

    private string Zone(HudPainter p, HudView v, int from, int to, Font font, float room)
    {
        var turns = v.Race.Turns.All;
        double a = from / (double)DuelLog.Pieces, b = (to + 1) / (double)DuelLog.Pieces;
        string? first = null, last = null, before = null;
        for (int i = 0; i < turns.Count; i++)
        {
            var (pct, name) = turns[i];
            if (pct >= a && pct < b) { first ??= name; last = name; }
            else if (pct < a) before = name;
        }
        string text = first is not null ? ReferenceEquals(first, last) ? first : PooledText.Concat(first, "–", last!)
            : before is not null ? PooledText.Concat("after ", before) : SectorName(v, a);
        return p.Measure(text, font) <= room ? text : SectorName(v, a);
    }

    private string SectorName(HudView v, double pct) => PooledText.Number("S", LapMath.SectorAt(_starts, pct) + 1);

    private string NextTurn(HudView v, int to)
    {
        double b = (to + 1) / (double)DuelLog.Pieces;
        var turns = v.Race.Turns.All;
        for (int i = 0; i < turns.Count; i++) if (turns[i].Pct >= b) return turns[i].Name;
        return turns.Count > 0 ? turns[0].Name : SectorName(v, b - Math.Floor(b));
    }

    private static (int Best, double BestV, int Worst, double WorstV) Swings(DuelSnapshot s)
    {
        int best = 0, worst = 0;
        double bv = 0, wv = 0;
        for (int i = 0; i <= DuelLog.Pieces - 3; i++)
        {
            double v = s.LastPiece[i] + s.LastPiece[i + 1] + s.LastPiece[i + 2];
            if (v < bv) { bv = v; best = i; }
            if (v > wv) { wv = v; worst = i; }
        }
        return (best, bv, worst, wv);
    }

    private float ZoneRoom(HudPainter p) => 120 * p.Density;

    private static readonly string[] RowsParts = { "pos", "num", "driver", "sectors", "last", "gap" };
    private static readonly string[] TickerParts = { "id", "swing", "trend" };
    private static readonly string[] CornersParts = { "head", "chart", "turns", "sectors", "foot" };
    private static readonly string[] MapParts = { "legend", "map", "foot" };
    private static readonly string[] BattleParts = { "gauge", "rivals" };
    private static readonly string[] TrendParts = { "chart", "foot" };
    private readonly RectangleF[] _cols = new RectangleF[6];

    private static void Place(HudPainter p, HudView v, string group, RectangleF area, bool row, float gap, string[] ids, ReadOnlySpan<float> sizes, Span<RectangleF> boxes, bool register)
    {
        if (register) p.Arrange(v, group, area, row, gap, ids, sizes, boxes);
        else HudArrange.Lay(area, row, gap, v.Options.Arranged(group, ids), sizes, boxes);
    }

    private void DrawRows(HudPainter p, HudView v)
    {
        var f = v.Frame;
        float d = p.Density;
        var font = p.Body;
        var bold = p.Bold;
        var cellFont = p.SmallBold;
        int n = Math.Max(1, _starts.Count);
        float gap = 6 * d, lead = 3 * d + 6 * d, cellGap = 3 * d;
        float posW = MathF.Ceiling(p.Measure("00", bold) + 8 * d);
        float numW = MathF.Ceiling(p.Measure("#000", font) + 2 * d);
        float cellW = MathF.Ceiling(Math.Max(p.Measure("−0.00", cellFont), p.Measure("00.00", cellFont)) + 12 * d);
        float lastW = MathF.Ceiling(p.Measure("0:00.00", cellFont) + 12 * d);
        float gapW = MathF.Ceiling(p.Measure("−0.000", bold) + 2 * d);
        float nameMin = WidestTag(p) + 6 * d + MathF.Ceiling(p.Measure("Megan Price", font));
        float rowH = MathF.Round(p.LineHeight(font) * 1.42f * d);
        float subH = MathF.Ceiling(p.LineHeight(p.Small) + 3 * d);
        float headH = MathF.Ceiling(p.LineHeight(p.Tiny) + 4 * d);
        float fixedW = lead + posW + gap + numW + gap + gap + n * cellW + (n - 1) * cellGap + gap + lastW + gap + gapW;
        p.NeedWidth(fixedW + nameMin + 16 * d);
        float full = headH + (rowH + subH) * 2 + rowH;
        p.NeedBelow(p.HeaderBody(), full);
        bool none = !_showAhead && !_showBehind;
        float content = headH + rowH + (_showAhead ? rowH + subH : 0) + (_showBehind ? rowH + subH : 0) + (none ? subH : 0);
        var area = p.Header("Head to head", WithinText(v), null, content);
        float nameW = Math.Max(40, area.Width - fixedW);
        float sectorsW = n * cellW + (n - 1) * cellGap;
        p.Arrange(v, "headtohead.rows", new RectangleF(area.X + lead, area.Y, area.Width - lead, content), true, gap, RowsParts, stackalloc float[] { posW, numW, nameW, sectorsW, lastW, gapW }, _cols);
        float posX = _cols[0].X, numX = _cols[1].X, nameX = _cols[2].X, cellsX = _cols[3].X, lastX = _cols[4].X, gapX = _cols[5].X;
        float y = area.Y;

        var tiny = p.Tiny;
        var faint = p.P.Faint;
        p.Text(p.LabelText("Pos"), tiny, faint, new RectangleF(posX, y, posW, headH), HAlign.Center, VAlign.Center, false);
        p.Text("#", tiny, faint, new RectangleF(numX, y, numW, headH), HAlign.Left, VAlign.Center, false);
        p.Text(p.LabelText("Driver"), tiny, faint, new RectangleF(nameX, y, nameW, headH), HAlign.Left, VAlign.Center, false);
        for (int i = 0; i < n; i++)
            p.Text(PooledText.Number("S", i + 1), tiny, faint, new RectangleF(cellsX + i * (cellW + cellGap), y, cellW, headH), HAlign.Center, VAlign.Center, false);
        p.Text(p.LabelText("Last lap"), tiny, faint, new RectangleF(lastX, y, lastW, headH), HAlign.Center, VAlign.Center, false);
        p.Text(p.LabelText("Gap"), tiny, faint, new RectangleF(gapX, y, gapW, headH), HAlign.Right, VAlign.Center, false);
        y += headH;

        void Columns(float rowY, string pos, string num)
        {
            p.Text(pos, bold, p.P.Heading, new RectangleF(posX, rowY, posW, rowH), HAlign.Center, VAlign.Center, false);
            p.Text(num, font, p.P.Muted, new RectangleF(numX, rowY, numW, rowH), HAlign.Left, VAlign.Center);
        }

        float cellY(float rowY) => rowY + MathF.Round(4 * d);
        float cellH = rowH - MathF.Round(8 * d);

        void Rival(DuelRole role, bool alternate)
        {
            var s = Of(role);
            var box = new RectangleF(area.X - 2 * d, y, area.Width + 4 * d, rowH + subH);
            if (alternate) p.Fill(box, HudPalette.Alpha(p.P.Heading, 7));
            p.Fill(new RectangleF(box.X, box.Y, 3 * d, box.Height), RoleColor(p, role));
            Columns(y, Position(f, s.Car), Number(f, s.Car));
            float cy = y + rowH / 2;
            float tx = Tag(p, role, nameX, cy) + 6 * d;
            string? flag = Flag(s);
            float flagW = flag is null ? 0 : ChipWidth(p, flag) + 6 * d;
            float nameRoom = Math.Max(0, nameX + nameW - tx - flagW);
            string name = RaceState.Name(f, s.Car);
            p.Text(name, bold, p.P.Heading, new RectangleF(tx, y, nameRoom, rowH), HAlign.Left, VAlign.Center);
            if (flag is not null) Chip(p, flag, tx + Math.Min(nameRoom, p.Measure(name, bold) + 2) + 6 * d, cy);
            for (int i = 0; i < n; i++)
                Cell(p, new RectangleF(cellsX + i * (cellW + cellGap), cellY(y), cellW, cellH), i < s.Sectors ? s.Sector[i] : DuelValue.Empty, false, v.Now);
            Cell(p, new RectangleF(lastX, cellY(y), lastW, cellH), s.LastLap is { } l ? new DuelValue(l, DuelState.Done) : DuelValue.Empty, false, v.Now);
            p.Text(GapText(role, s.Gap), bold, RoleColor(p, role), new RectangleF(gapX, y, gapW, rowH), HAlign.Right, VAlign.Center, false);
            TrendRuns(p, s, role, true);
            DrawRuns(p, new RectangleF(nameX, y + rowH - 3 * d, area.Right - nameX, subH));
            y += rowH + subH;
        }

        if (_showAhead) Rival(DuelRole.Ahead, false);
        {
            int me = f.PlayerCarIdx;
            var box = new RectangleF(area.X - 2 * d, y, area.Width + 4 * d, rowH);
            p.Fill(box, p.Solid(HudPalette.Deep(p.P.RowTint, 0.25f, 0.8f), 240));
            if (p.P.HighlightEdge is { } edge) p.Outline(new RectangleF(box.X + 0.5f, box.Y + 0.5f, box.Width - 1, box.Height - 1), edge);
            Columns(y, Position(f, me), Number(f, me));
            p.Text(RaceState.Name(f, me), bold, p.P.PlayerText, new RectangleF(nameX, y, nameW, rowH), HAlign.Left, VAlign.Center);
            for (int i = 0; i < n; i++)
                Cell(p, new RectangleF(cellsX + i * (cellW + cellGap), cellY(y), cellW, cellH), i < _mine.Count ? _mine.Sector[i] : DuelValue.Empty, true, v.Now);
            Cell(p, new RectangleF(lastX, cellY(y), lastW, cellH), _mine.LastLap is { } ml ? new DuelValue(ml, DuelState.Done) : DuelValue.Empty, true, v.Now);
            p.Text(RaceFormat.Missing, bold, p.P.PlayerText, new RectangleF(gapX, y, gapW, rowH), HAlign.Right, VAlign.Center, false);
            y += rowH;
        }
        if (_showBehind) Rival(DuelRole.Behind, _showAhead);
        if (none) p.Text(Nobody(v), p.Small, p.P.Faint, new RectangleF(area.X, y, area.Width, subH), HAlign.Center, VAlign.Center);
    }

    private void DrawTicker(HudPainter p, HudView v)
    {
        var f = v.Frame;
        float d = p.Density, pad = 8 * d, sepGap = 9 * d;
        float h = MathF.Ceiling(p.LineHeight(p.SmallBold) + 12 * d), between = 4 * d;
        var gapFont = p.Bold;
        float natural = 3 * d + pad + WidestTag(p) + 8 * d + p.Measure("#000", p.SmallBold) + 6 * d + p.Measure("−0.000", gapFont) + 6 * d + ChipWidth(p, "Off track")
            + sepGap * 2 + p.Measure("S10 −0.00", p.SmallBold) + sepGap * 2 + p.Measure("Dropping back 0.00", p.SmallBold) + pad;
        p.Need(MathF.Ceiling(natural), h * 2 + between);
        float y = 0;
        bool any = false, register = true;
        Span<float> sizes = stackalloc float[3];
        Span<RectangleF> parts = stackalloc RectangleF[3];
        var small = p.SmallBold;
        foreach (var role in Roles)
        {
            if (!Shown(role)) continue;
            any = true;
            var s = Of(role);
            var box = new RectangleF(0, y, p.Bounds.Width, h);
            p.Panel(box);
            p.Fill(new RectangleF(box.X, box.Y, 3 * d, box.Height), RoleColor(p, role));
            float cy = box.Y + h / 2, left = box.X + 3 * d + pad, right = box.Right - pad;
            string num = Number(f, s.Car);
            float numW = p.Measure(num, small);
            string gap = GapText(role, s.Gap);
            float gapW = p.Measure(gap, gapFont);
            string? flag = Flag(s);
            sizes[0] = TagWidth(p, role) + 8 * d + numW + 6 * d + gapW + (flag is null ? 0 : 6 * d + ChipWidth(p, flag));
            int big = 0;
            if (s.HasLastSectors)
            {
                for (int i = 1; i < s.Sectors; i++) if (Math.Abs(s.LastSector[i]) > Math.Abs(s.LastSector[big])) big = i;
                sizes[1] = p.Measure(PooledText.Number("S", big + 1), small) + Space(p, small) + p.Measure(Delta(s.LastSector[big]), small);
            }
            else sizes[1] = -1;
            if (s.TrendLaps > 0)
            {
                TrendRuns(p, s, role, false);
                sizes[2] = RunsWidth(p);
            }
            else sizes[2] = -1;
            Place(p, v, "headtohead.ticker", new RectangleF(left, box.Y, right - left, h), true, sepGap * 2, TickerParts, sizes, parts, register);
            register = false;
            float leftmost = float.MaxValue;
            for (int i = 0; i < parts.Length; i++) if (sizes[i] >= 0) leftmost = Math.Min(leftmost, parts[i].X);
            for (int i = 0; i < parts.Length; i++)
            {
                if (sizes[i] < 0 || parts[i].X <= leftmost) continue;
                float sx = parts[i].X - sepGap;
                p.Line(sx, box.Y + 6 * d, sx, box.Bottom - 6 * d, p.P.LineStrong);
            }
            if (sizes[2] >= 0) DrawRuns(p, new RectangleF(parts[2].X, box.Y, right - parts[2].X, h));
            if (sizes[1] >= 0)
            {
                Run(PooledText.Number("S", big + 1), small, DeltaColor(p, s.LastSector[big]), false);
                Run(Delta(s.LastSector[big]), small, DeltaColor(p, s.LastSector[big]));
                DrawRuns(p, new RectangleF(parts[1].X, box.Y, box.Right - parts[1].X, h));
            }
            float x = Tag(p, role, parts[0].X, cy) + 8 * d;
            p.Text(num, small, p.P.Muted, new RectangleF(x, box.Y, numW + 2, h), HAlign.Left, VAlign.Center, false);
            x += numW + 6 * d;
            p.Text(gap, gapFont, RoleColor(p, role), new RectangleF(x, box.Y, gapW + 2, h), HAlign.Left, VAlign.Center, false);
            x += gapW + 6 * d;
            if (flag is not null) Chip(p, flag, x, cy);
            y += h + between;
        }
        if (!any)
        {
            var box = new RectangleF(0, 0, p.Bounds.Width, h);
            p.Panel(box);
            p.Text(Nobody(v), p.Small, p.P.Faint, box.Inflated(-pad, 0), HAlign.Left, VAlign.Center);
        }
    }

    private float FootLineH(HudPainter p) => MathF.Ceiling(p.LineHeight(p.Small) + 3 * p.Density);

    private void FootLine(HudPainter p, RectangleF line, DuelRole role)
    {
        p.Fill(new RectangleF(line.X, line.Y + 2 * p.Density, 2 * p.Density, line.Height - 4 * p.Density), RoleColor(p, role));
        DrawRuns(p, new RectangleF(line.X + 8 * p.Density, line.Y, line.Width - 8 * p.Density, line.Height));
    }

    private void DrawCorners(HudPainter p, HudView v)
    {
        var f = v.Frame;
        float d = p.Density;
        float width = 320 * d;
        float headH = MathF.Ceiling(p.LineHeight(p.Bold) + 8 * d);
        float labelsH = MathF.Ceiling(p.LineHeight(p.Tiny) + 3 * d);
        float boxH = MathF.Ceiling(p.LineHeight(p.SmallBold) + 6 * d);
        float footH = FootLineH(p);
        float Block(float chart) => headH + chart + labelsH + 4 * d + boxH + 6 * d + footH * 2 + 8 * d;
        float twoChart = 64 * d, oneChart = 86 * d;
        p.NeedWidth(width + 16 * d);
        p.NeedBelow(p.HeaderBody(), Block(twoChart) * 2);
        int shown = ShownCount;
        if (shown == 0) { DrawEmpty(p, v, "Corners"); return; }
        float chartH = shown == 2 ? twoChart : oneChart;
        var area = p.Header("Corners", WithinText(v), null, Block(chartH) * shown - 8 * d);
        float y = area.Y;
        int n = Math.Max(1, _starts.Count);
        Span<float> sizes = stackalloc float[] { headH, chartH, labelsH + 4 * d, boxH + 6 * d, footH * 2 };
        Span<RectangleF> parts = stackalloc RectangleF[5];
        bool register = true;
        foreach (var role in Roles)
        {
            if (!Shown(role)) continue;
            var s = Of(role);
            var c = RoleColor(p, role);
            Place(p, v, "headtohead.corners", new RectangleF(area.X, y, area.Width, Block(chartH) - 8 * d), false, 0, CornersParts, sizes, parts, register);
            register = false;
            float headY = parts[0].Y;
            float cy = headY + headH / 2;
            float tx = Tag(p, role, area.X, cy) + 6 * d;
            string gap = GapText(role, s.Gap);
            float gw = p.Measure(gap, p.Bold) + 2;
            Run(Number(f, s.Car), p.Bold, p.P.Heading);
            Run(RaceState.Name(f, s.Car), p.Bold, p.P.Heading);
            DrawRuns(p, new RectangleF(tx, headY, area.Right - tx - gw - 6 * d, headH));
            p.Text(gap, p.Bold, c, new RectangleF(area.Right - gw, headY, gw, headH), HAlign.Right, VAlign.Center, false);
            PieceChart(p, v, s, new RectangleF(area.X, parts[1].Y, area.Width, chartH), c);
            TurnLabels(p, v, new RectangleF(area.X, parts[2].Y, area.Width, labelsH));
            float bw = (area.Width - 4 * d * (n - 1)) / n;
            for (int i = 0; i < n; i++) Cell(p, new RectangleF(area.X + i * (bw + 4 * d), parts[3].Y, bw, boxH), i < s.Sectors ? s.Sector[i] : DuelValue.Empty, false, v.Now);
            float footY = parts[4].Y;
            if (s.HasLastPieces)
            {
                var (best, bv, worst, wv) = Swings(s);
                Run("Last lap · gained most", p.Small, p.P.Muted);
                Run(Zone(p, v, best, best + 2, p.SmallBold, ZoneRoom(p)), p.SmallBold, p.P.Heading);
                Run(Delta(bv), p.SmallBold, p.P.GoodText);
                FootLine(p, new RectangleF(area.X, footY, area.Width, footH), role);
                Run("Lost most", p.Small, p.P.Muted);
                Run(Zone(p, v, worst, worst + 2, p.SmallBold, ZoneRoom(p)), p.SmallBold, p.P.Heading);
                Run(Delta(wv), p.SmallBold, p.P.DangerText);
                FootLine(p, new RectangleF(area.X, footY + footH, area.Width, footH), role);
            }
            else
            {
                Run("Gained and lost corners after one full lap", p.Small, p.P.Faint);
                FootLine(p, new RectangleF(area.X, footY, area.Width, footH), role);
            }
            y += Block(chartH);
        }
    }

    private void PieceChart(HudPainter p, HudView v, DuelSnapshot s, RectangleF r, Color frame)
    {
        float d = p.Density, radius = p.CornerRadius(4);
        p.Fill(r, p.Solid(p.Sunken(0.35), 230), radius);
        var starts = _starts;
        for (int i = 1; i < starts.Count; i++)
        {
            float x = r.X + (float)starts[i] * r.Width;
            p.Line(x, r.Y + 1, x, r.Bottom - 1, p.P.LineStrong);
        }
        float mid = r.Y + r.Height / 2;
        p.Line(r.X + 1, mid, r.Right - 1, mid, HudPalette.Alpha(p.P.Muted, 90));
        float bw = r.Width / DuelLog.Pieces, half = r.Height / 2 - 5 * d;
        for (int i = 0; i < DuelLog.Pieces; i++)
        {
            var c = s.Piece[i];
            if (c.State == DuelState.None) continue;
            float h = Math.Max(1, (float)Math.Min(1, Math.Abs(c.Value) / PieceScale) * half);
            var color = Math.Abs(c.Value) < PieceEven ? p.P.Faint : c.Value < 0 ? p.P.Good : p.P.Danger;
            if (c.State == DuelState.Ghost) color = HudPalette.Alpha(color, 72);
            p.Fill(new RectangleF(r.X + i * bw + 1.5f * d, c.Value < 0 ? mid - h : mid, Math.Max(1, bw - 3 * d), h), color, 1);
        }
        float cur = r.X + s.CurrentPiece * bw;
        p.Outline(new RectangleF(cur + 0.75f, r.Y + 0.75f, bw - 1.5f, r.Height - 1.5f), p.P.Accent, 1.5f, 2);
        p.Outline(new RectangleF(r.X + 0.75f, r.Y + 0.75f, r.Width - 1.5f, r.Height - 1.5f), frame, 1.5f, radius);
        float lh = p.LineHeight(p.Tiny);
        p.Text(p.LabelText("You gain"), p.Tiny, p.P.GoodText, new RectangleF(r.X + 5 * d, r.Y + 2 * d, r.Width / 3, lh), HAlign.Left, VAlign.Center, false);
        p.Text(p.LabelText("You lose"), p.Tiny, p.P.DangerText, new RectangleF(r.X + 5 * d, r.Bottom - 2 * d - lh, r.Width / 3, lh), HAlign.Left, VAlign.Center, false);
    }

    private void TurnLabels(HudPainter p, HudView v, RectangleF r)
    {
        var turns = v.Race.Turns.All;
        float right = float.NegativeInfinity;
        if (turns.Count == 0)
        {
            var starts = _starts;
            for (int i = 0; i < starts.Count; i++)
            {
                double end = i + 1 < starts.Count ? starts[i + 1] : 1;
                float cx = r.X + (float)((starts[i] + end) / 2) * r.Width;
                string label = PooledText.Number("S", i + 1);
                float w = p.Measure(label, p.Tiny) + 2;
                p.Text(label, p.Tiny, p.P.Faint, new RectangleF(cx - w / 2, r.Y, w, r.Height), HAlign.Center, VAlign.Center, false);
            }
            return;
        }
        for (int i = 0; i < turns.Count; i++)
        {
            var (pct, name) = turns[i];
            float w = p.Measure(name, p.Tiny) + 2;
            float x = Math.Clamp(r.X + (float)pct * r.Width - w / 2, r.X, r.Right - w);
            if (x < right + 3 * p.Density) continue;
            p.Text(name, p.Tiny, p.P.Faint, new RectangleF(x, r.Y, w, r.Height), HAlign.Center, VAlign.Center, false);
            right = x + w;
        }
    }

    private TrackMap? _builtFor;
    private RectangleF _builtIn;
    private float _builtDensity;
    private readonly PointF[] _centre = new PointF[MapSamples + 1], _normal = new PointF[MapSamples + 1];
    private readonly PointF[][] _laneOut = Lanes(), _laneIn = Lanes(), _laneMid = Lanes();
    private readonly PointF[] _edgeOut = new PointF[MapSamples + 1], _edgeIn = new PointF[MapSamples + 1], _edgeOutWide = new PointF[MapSamples + 1], _edgeInWide = new PointF[MapSamples + 1];
    private float _outside = 1;
    private PointF _middle;
    private readonly List<RectangleF> _labels = new();

    private static PointF[][] Lanes()
    {
        var lanes = new PointF[DuelLog.Pieces][];
        for (int i = 0; i < lanes.Length; i++) lanes[i] = new PointF[SamplesPerPiece + 1];
        return lanes;
    }

    private void BuildMap(HudPainter p, TrackMap map, RectangleF room)
    {
        if (ReferenceEquals(map, _builtFor) && room == _builtIn && p.Density == _builtDensity) return;
        _builtFor = map;
        _builtIn = room;
        _builtDensity = p.Density;
        float d = p.Density;
        var extent = map.Bounds;
        float margin = 22 * d;
        float scale = MapView.FitScale(extent, new SizeF(Math.Max(10, room.Width - margin * 2), Math.Max(10, room.Height - margin * 2)), false);
        var view = new MapView(new PointF(room.X + room.Width / 2, room.Y + room.Height / 2), new PointF(extent.X + extent.Width / 2, extent.Y + extent.Height / 2), scale, 0);
        _middle = view.Screen;
        for (int i = 0; i <= MapSamples; i++) _centre[i] = view.ToScreen(map.At(i / (double)MapSamples));
        double area = 0;
        for (int i = 0; i < MapSamples; i++) area += _centre[i].X * _centre[i + 1].Y - _centre[i + 1].X * _centre[i].Y;
        _outside = area > 0 ? -1 : 1;
        for (int i = 0; i <= MapSamples; i++)
        {
            var a = _centre[Math.Max(0, i - 1)];
            var b = _centre[Math.Min(MapSamples, i + 1)];
            if (i == 0) a = _centre[MapSamples - 1];
            if (i == MapSamples) b = _centre[1];
            float dx = b.X - a.X, dy = b.Y - a.Y, len = MathF.Max(0.001f, MathF.Sqrt(dx * dx + dy * dy));
            _normal[i] = new PointF(-dy / len * _outside, dx / len * _outside);
        }
        PointF Off(int i, float by) => new(_centre[i].X + _normal[i].X * by, _centre[i].Y + _normal[i].Y * by);
        for (int piece = 0; piece < DuelLog.Pieces; piece++)
            for (int j = 0; j <= SamplesPerPiece; j++)
            {
                int i = piece * SamplesPerPiece + j;
                _laneMid[piece][j] = _centre[i];
                _laneOut[piece][j] = Off(i, 3.6f * d);
                _laneIn[piece][j] = Off(i, -3.6f * d);
            }
        for (int i = 0; i <= MapSamples; i++)
        {
            _edgeOut[i] = Off(i, 6.2f * d);
            _edgeIn[i] = Off(i, -6.2f * d);
            _edgeOutWide[i] = Off(i, 8.2f * d);
            _edgeInWide[i] = Off(i, -8.2f * d);
        }
    }

    private PointF MapPoint(double pct, out PointF normal)
    {
        double x = (pct - Math.Floor(pct)) * MapSamples;
        int i = Math.Clamp((int)x, 0, MapSamples - 1);
        float u = (float)(x - i);
        var a = _centre[i];
        var b = _centre[i + 1];
        normal = _normal[i];
        return new PointF(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u);
    }

    private void DrawMap(HudPainter p, HudView v)
    {
        var f = v.Frame;
        float d = p.Density;
        float width = 320 * d, mapH = 170 * d;
        float legendH = MathF.Ceiling(p.LineHeight(p.SmallBold) + 6 * d);
        float footH = FootLineH(p);
        float Body(int rivals) => legendH * rivals + 4 * d + mapH + 6 * d + footH * 2 * rivals;
        p.NeedWidth(width + 16 * d);
        p.NeedBelow(p.HeaderBody(), Body(2));
        int shown = ShownCount;
        if (shown == 0) { DrawEmpty(p, v, "Track"); return; }
        var area = p.Header("Track", WithinText(v), null, Body(shown));
        Span<RectangleF> parts = stackalloc RectangleF[3];
        p.Arrange(v, "headtohead.map", new RectangleF(area.X, area.Y, area.Width, Body(shown)), false, 0, MapParts, stackalloc float[] { legendH * shown + 4 * d, mapH + 6 * d, footH * 2 * shown }, parts);
        float y = parts[0].Y;
        bool two = shown == 2;
        foreach (var role in Roles)
        {
            if (!Shown(role)) continue;
            var s = Of(role);
            float x = Tag(p, role, area.X, y + legendH / 2) + 6 * d;
            Run(Number(f, s.Car), p.SmallBold, p.P.Heading);
            Run(GapText(role, s.Gap), p.SmallBold, RoleColor(p, role));
            if (two)
            {
                Run("·", p.Small, p.P.Faint);
                Run(role == DuelRole.Ahead ? "outside lane" : "inside lane", p.Small, p.P.Muted);
            }
            DrawRuns(p, new RectangleF(x, y, area.Right - x, legendH));
            y += legendH;
        }
        var room = new RectangleF(area.X, parts[1].Y, area.Width, mapH);
        var map = _maps.Get(p, f);
        if (map is null)
        {
            p.Fill(room, p.Solid(p.Sunken(0.35), 200), p.CornerRadius(4));
            p.Text(_maps.Loading ? "Loading the map" : "No map for this track", p.Body, p.P.Muted, room, HAlign.Center, VAlign.Center);
        }
        else
        {
            BuildMap(p, map, room);
            var g = p.G;
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var trackColor = p.P.MapTrack ?? HudPalette.Mix(p.P.Surface, p.P.Text, 0.2);
            var basePen = p.Pen(trackColor, (two ? 15 : 11) * d);
            g.DrawLines(basePen, _centre);
            if (two)
            {
                g.DrawLines(p.Pen(HudPalette.Alpha(p.P.Attack, 150), 1), _edgeOutWide);
                g.DrawLines(p.Pen(HudPalette.Alpha(p.P.Defend, 150), 1), _edgeInWide);
            }
            else
            {
                var edge = HudPalette.Alpha(RoleColor(p, _showAhead ? DuelRole.Ahead : DuelRole.Behind), 150);
                g.DrawLines(p.Pen(edge, 1), _edgeOut);
                g.DrawLines(p.Pen(edge, 1), _edgeIn);
            }
            foreach (var role in Roles)
            {
                if (!Shown(role)) continue;
                var s = Of(role);
                var lanes = !two ? _laneMid : role == DuelRole.Ahead ? _laneOut : _laneIn;
                float w = (two ? 3.4f : 6) * d;
                for (int i = 0; i < DuelLog.Pieces; i++)
                {
                    var c = s.Piece[i];
                    if (c.State == DuelState.None) continue;
                    var color = Math.Abs(c.Value) < PieceEven ? p.P.Faint : c.Value < 0 ? p.P.Good : p.P.Danger;
                    if (c.State == DuelState.Ghost) color = HudPalette.Alpha(color, 80);
                    g.DrawLines(p.FlatPen(color, w), lanes[i]);
                }
            }
            var starts = _starts;
            for (int i = 0; i < starts.Count; i++)
            {
                var at = MapPoint(starts[i], out var nrm);
                float reach = (two ? 10 : 8) * d;
                p.Line(at.X - nrm.X * reach, at.Y - nrm.Y * reach, at.X + nrm.X * reach, at.Y + nrm.Y * reach, i == 0 ? p.P.Heading : p.P.AccentText, i == 0 ? 2 * d : 1.5f * d);
            }
            _labels.Clear();
            var turns = v.Race.Turns.All;
            for (int i = 0; i < turns.Count; i++)
            {
                var (pct, name) = turns[i];
                var at = MapPoint(pct, out var nrm);
                float tw = p.Measure(name, p.Tiny) + 2, th = p.LineHeight(p.Tiny);
                float reach = 16 * d;
                var label = new RectangleF(at.X + nrm.X * reach - tw / 2, at.Y + nrm.Y * reach - th / 2, tw, th);
                label.X = Math.Clamp(label.X, room.X, room.Right - tw);
                label.Y = Math.Clamp(label.Y, room.Y, room.Bottom - th);
                bool clash = false;
                foreach (var l in _labels) if (l.IntersectsWith(label)) { clash = true; break; }
                if (clash) continue;
                _labels.Add(label);
                p.Text(name, p.Tiny, p.P.Faint, label, HAlign.Center, VAlign.Center, false);
            }
            foreach (var role in Roles)
            {
                if (!Shown(role)) continue;
                int car = Of(role).Car;
                if ((uint)car >= TelemetryFrame.MaxCars || f.CarLapDistPct[car] < 0) continue;
                var at = MapPoint(f.CarLapDistPct[car], out _);
                p.Dot(at.X, at.Y, 6.5f * d, p.P.Bg);
                p.Dot(at.X, at.Y, 5 * d, RoleColor(p, role));
            }
            if (f.PlayerCarIdx is var me && (uint)me < TelemetryFrame.MaxCars && f.CarLapDistPct[me] >= 0)
            {
                var at = MapPoint(f.CarLapDistPct[me], out _);
                p.Dot(at.X, at.Y, 7 * d, p.P.Heading);
                p.Dot(at.X, at.Y, 5.5f * d, p.P.Accent);
            }
            g.SmoothingMode = old;
        }
        y = parts[2].Y;
        foreach (var role in Roles)
        {
            if (!Shown(role)) continue;
            var s = Of(role);
            var c = RoleColor(p, role);
            if (!s.HasLastPieces)
            {
                Run(Number(f, s.Car), p.SmallBold, c);
                Run("needs one full lap", p.Small, p.P.Faint);
                FootLine(p, new RectangleF(area.X, y, area.Width, footH), role);
                Run("", p.Small, p.P.Faint);
                FootLine(p, new RectangleF(area.X, y + footH, area.Width, footH), role);
                y += footH * 2;
                continue;
            }
            var (best, bv, worst, wv) = Swings(s);
            bool ahead = role == DuelRole.Ahead;
            int from = ahead ? best : worst;
            Run(Number(f, s.Car), p.SmallBold, c);
            Run(ahead ? "you're quicker" : "they're quicker", p.Small, p.P.Muted);
            Run(Zone(p, v, from, from + 2, p.SmallBold, ZoneRoom(p)), p.SmallBold, p.P.Heading);
            Run(Delta(ahead ? bv : wv), p.SmallBold, ahead ? p.P.GoodText : p.P.DangerText);
            FootLine(p, new RectangleF(area.X, y, area.Width, footH), role);
            Run(ahead ? "Pass into" : "Cover into", p.SmallBold, p.P.AccentText);
            Run(NextTurn(v, from + 2), p.SmallBold, p.P.AccentText);
            FootLine(p, new RectangleF(area.X, y + footH, area.Width, footH), role);
            y += footH * 2;
        }
    }

    private void DrawBattle(HudPainter p, HudView v)
    {
        var f = v.Frame;
        float d = p.Density;
        float width = 320 * d, gaugeH = 62 * d;
        float nameH = MathF.Ceiling(p.LineHeight(p.Bold) + 6 * d);
        float lineH = FootLineH(p);
        float rowH = nameH + lineH * 3 + 6 * d;
        p.NeedWidth(width + 16 * d);
        p.NeedBelow(p.HeaderBody(), gaugeH + 6 * d + rowH * 2);
        float rivalsH = rowH * Math.Max(1, ShownCount);
        var area = p.Header("Battle", WithinText(v), null, gaugeH + 6 * d + rivalsH);
        Span<RectangleF> parts = stackalloc RectangleF[2];
        p.Arrange(v, "headtohead.battle", new RectangleF(area.X, area.Y, area.Width, gaugeH + 6 * d + rivalsH), false, 0, BattleParts, stackalloc float[] { gaugeH + 6 * d, rivalsH }, parts);
        float top = parts[0].Y;
        double within = v.Options.Number("within");
        float cx = area.X + area.Width / 2, half = area.Width / 2 - 22 * d;
        double span = Math.Max(within * 1.35, 1.5);
        float X(double s) => cx + (float)(s / span) * half;
        float barY = top + 24 * d, barH = 10 * d;
        p.Fill(new RectangleF(area.X, barY, area.Width, barH), p.Solid(p.Sunken(0.35), 230), p.CornerRadius(5));
        p.Fill(RectangleF.FromLTRB(X(-within), barY, X(within), barY + barH), HudPalette.Alpha(p.P.Accent, 40));
        p.Fill(RectangleF.FromLTRB(X(-InRange), barY, X(InRange), barY + barH), HudPalette.Alpha(p.P.Accent, 80));
        p.Fill(new RectangleF(cx - 2 * d, barY - 7 * d, 4 * d, barH + 14 * d), p.P.Accent, 2 * d);
        foreach (var role in Roles)
        {
            var s = Of(role);
            if (s.Car < 0 || !s.HasGap) continue;
            bool on = Shown(role), ahead = role == DuelRole.Ahead;
            int dir = ahead ? 1 : -1;
            double gap = Math.Min(Math.Abs(s.Gap), span);
            float x = X(dir * gap);
            var color = on ? RoleColor(p, role) : HudPalette.Alpha(p.P.Faint, 150);
            float cy = barY + barH / 2;
            p.Dot(x, cy, 7 * d, p.P.Bg);
            p.Dot(x, cy, 6 * d, color);
            string label = PooledText.Concat(p.LabelText(RoleWord(role)), " ", Number(f, s.Car));
            float lw = p.Measure(label, p.Label) + 2;
            float lx = ahead ? Math.Max(cx + 5 * d, x - lw / 2) : Math.Min(cx - 5 * d - lw, x - lw / 2);
            lx = Math.Clamp(lx, area.X, area.Right - lw);
            p.Text(label, p.Label, color, new RectangleF(lx, top, lw, 16 * d), HAlign.Left, VAlign.Center, false);
            string gapText = GapText(role, s.Gap);
            float gw = p.Measure(gapText, p.SmallBold) + 2;
            float gx = ahead ? Math.Max(cx + 5 * d, x - gw / 2) : Math.Min(cx - 5 * d - gw, x - gw / 2);
            gx = Math.Clamp(gx, area.X, area.Right - gw);
            p.Text(gapText, p.SmallBold, color, new RectangleF(gx, barY + barH + 6 * d, gw, p.LineHeight(p.SmallBold)), HAlign.Left, VAlign.Center, false);
            double closing = s.Closing(role);
            if (on && Math.Abs(closing) > Even)
            {
                bool towards = closing > 0;
                bool good = ahead ? towards : !towards;
                bool pointsLeft = ahead == towards;
                float tip = pointsLeft ? x - 10 * d : x + 10 * d;
                Pointer(p, tip, cy, 8 * d, pointsLeft, good ? p.P.Good : p.P.Danger);
            }
        }
        float y = parts[1].Y;
        if (ShownCount == 0)
        {
            p.Text(Nobody(v), p.Small, p.P.Faint, new RectangleF(area.X, y, area.Width, lineH), HAlign.Center, VAlign.Center);
            return;
        }
        bool alt = false;
        foreach (var role in Roles)
        {
            if (!Shown(role)) continue;
            var s = Of(role);
            var box = new RectangleF(area.X - 2 * d, y, area.Width + 4 * d, rowH);
            if (alt) p.Fill(box, HudPalette.Alpha(p.P.Heading, 7));
            alt = true;
            p.Fill(new RectangleF(box.X, box.Y, 3 * d, box.Height), RoleColor(p, role));
            float tx = Tag(p, role, area.X + 6 * d, y + 3 * d + nameH / 2) + 6 * d;
            Run(Number(f, s.Car), p.Bold, p.P.Heading);
            Run(RaceState.Name(f, s.Car), p.Bold, p.P.Heading);
            DrawRuns(p, new RectangleF(tx, y + 3 * d, area.Right - tx, nameH));
            float ly = y + 3 * d + nameH;
            var line = new RectangleF(area.X + 6 * d, ly, area.Width - 6 * d, lineH);
            if (!s.HasLastPieces)
            {
                Run("Corners after one full lap", p.Small, p.P.Faint);
                DrawRuns(p, line);
            }
            else
            {
                var (best, bv, worst, wv) = Swings(s);
                bool ahead = role == DuelRole.Ahead;
                if (bv < -Even)
                {
                    Run("You're quicker", p.Small, p.P.Muted);
                    Run(Zone(p, v, best, best + 2, p.SmallBold, ZoneRoom(p)), p.SmallBold, p.P.GoodText);
                    Run(Delta(bv), p.SmallBold, p.P.GoodText);
                    DrawRuns(p, line);
                    line.Y += lineH;
                }
                if (wv > Even)
                {
                    Run("They're quicker", p.Small, p.P.Muted);
                    Run(Zone(p, v, worst, worst + 2, p.SmallBold, ZoneRoom(p)), p.SmallBold, p.P.DangerText);
                    Run(Delta(wv), p.SmallBold, p.P.DangerText);
                    DrawRuns(p, line);
                    line.Y += lineH;
                }
                if (bv >= -Even && wv <= Even)
                {
                    Run("Even everywhere", p.Small, p.P.Muted);
                    DrawRuns(p, line);
                }
                else if (ahead && bv < -Even)
                {
                    Run("Attack into", p.SmallBold, p.P.AccentText);
                    Run(NextTurn(v, best + 2), p.SmallBold, p.P.AccentText);
                    DrawRuns(p, line);
                }
                else if (!ahead && wv > Even)
                {
                    Run("Defend into", p.SmallBold, p.P.AccentText);
                    Run(NextTurn(v, worst + 2), p.SmallBold, p.P.AccentText);
                    DrawRuns(p, line);
                }
            }
            y += rowH;
        }
    }

    private readonly PointF[][] _history = Enumerable.Range(0, DuelSnapshot.HistoryLaps + 2).Select(n => new PointF[n]).ToArray();

    private void DrawTrend(HudPainter p, HudView v)
    {
        var f = v.Frame;
        float d = p.Density;
        float width = 320 * d, chartH = 130 * d, labelsH = MathF.Ceiling(p.LineHeight(p.Tiny) + 3 * d);
        float footH = FootLineH(p);
        p.NeedWidth(width + 16 * d);
        p.NeedBelow(p.HeaderBody(), chartH + labelsH + 6 * d + footH * 4);
        int lap = f.Lap ?? (f.LapCompleted ?? 0) + 1;
        var togo = v.Race.LapsToGoEstimate(f);
        int left = togo is { } t ? Math.Max(0, (int)Math.Floor(t.Laps + 1e-6)) : -1;
        string right = left >= 0
            ? PooledText.Concat(PooledText.Number("Lap ", lap), " · ", togo!.Value.Forecast ? RaceFormat.About(PooledText.Number("", left, " left")) : PooledText.Number("", left, " left"))
            : PooledText.Number("Lap ", lap);
        int lines = 0;
        foreach (var role in Roles) if (Shown(role)) lines += 2;
        float chartPartH = chartH + labelsH + 6 * d, footPartH = footH * Math.Max(1, lines);
        var area = p.Header("Gap trend", right, null, chartPartH + footPartH);
        Span<RectangleF> parts = stackalloc RectangleF[2];
        p.Arrange(v, "headtohead.trend", new RectangleF(area.X, area.Y, area.Width, chartPartH + footPartH), false, 0, TrendParts, stackalloc float[] { chartPartH, footPartH }, parts);
        double within = v.Options.Number("within");
        double maxX = 0, maxGap = within * 1.2;
        foreach (var role in Roles)
        {
            var s = Of(role);
            if (!Shown(role)) continue;
            for (int i = 0; i < s.HistoryCount; i++)
            {
                maxX = Math.Max(maxX, s.HistoryX[i]);
                maxGap = Math.Max(maxGap, Math.Min(8, Math.Abs(s.HistoryGap[i]) * 1.1));
            }
        }
        float axis = MathF.Ceiling(p.Measure("YOU", p.Tiny) + 8 * d);
        var chart = new RectangleF(area.X + axis, parts[0].Y, area.Width - axis, chartH);
        double l0 = Math.Max(0, Math.Floor(maxX) - 7), l1 = l0 + 8;
        float X(double x) => chart.X + 4 * d + (float)((x - l0) / (l1 - l0)) * (chart.Width - 8 * d);
        float mid = chart.Y + chart.Height / 2, halfH = chart.Height / 2 - 12 * d;
        float Y(double g) => mid - (float)(Math.Clamp(g, -maxGap, maxGap) / maxGap) * halfH;
        p.Fill(chart, p.Solid(p.Sunken(0.35), 230), p.CornerRadius(4));
        p.Fill(RectangleF.FromLTRB(chart.X, Y(within), chart.Right, Y(-within)), HudPalette.Alpha(p.P.Accent, 34));
        p.Line(chart.X + 1, mid, chart.Right - 1, mid, p.P.Accent, 1.5f);
        float lh = p.LineHeight(p.Tiny);
        p.Text(p.LabelText("You"), p.Tiny, p.P.AccentText, new RectangleF(area.X, mid - lh / 2, axis - 4 * d, lh), HAlign.Right, VAlign.Center, false);
        foreach (double g in stackalloc double[] { maxGap * 0.8, -maxGap * 0.8 })
            p.Text(PooledText.Number(g > 0 ? "+" : "−", Math.Abs(g), "0.0", Inv), p.Tiny, p.P.Faint, new RectangleF(area.X, Y(g) - lh / 2, axis - 4 * d, lh), HAlign.Right, VAlign.Center, false);
        float lastRight = float.NegativeInfinity;
        for (double l = l0; l <= l1; l++)
        {
            string label = PooledText.Number("L", (long)l + 1);
            float w = p.Measure(label, p.Tiny) + 2, x = Math.Clamp(X(l) - w / 2, chart.X, chart.Right - w);
            if (x < lastRight + 2 * d) continue;
            p.Text(label, p.Tiny, p.P.Faint, new RectangleF(x, chart.Bottom + 2 * d, w, lh), HAlign.Center, VAlign.Center, false);
            lastRight = x + w;
        }
        var g2 = p.G;
        var mode = g2.SmoothingMode;
        g2.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var role in Roles)
        {
            var s = Of(role);
            if (!Shown(role) || s.HistoryCount == 0) continue;
            int sign = role == DuelRole.Ahead ? 1 : -1;
            int start = 0;
            while (start < s.HistoryCount && s.HistoryX[start] < l0) start++;
            int count = s.HistoryCount - start;
            if (count <= 0) continue;
            var pts = _history[count];
            for (int i = 0; i < count; i++) pts[i] = new PointF(X(s.HistoryX[start + i]), Y(sign * Math.Abs(s.HistoryGap[start + i])));
            var c = RoleColor(p, role);
            if (count > 1) g2.DrawLines(p.Pen(c, 2 * d), pts);
            p.Dot(pts[count - 1].X, pts[count - 1].Y, 3.5f * d, c);
        }
        g2.SmoothingMode = mode;
        foreach (var role in Roles)
        {
            if (!Shown(role)) continue;
            string label = PooledText.Concat(p.LabelText(RoleWord(role)), " ", Number(f, Of(role).Car));
            float w = p.Measure(label, p.Label) + 2;
            float ly = role == DuelRole.Ahead ? chart.Y + 3 * d : chart.Bottom - 3 * d - p.LineHeight(p.Label);
            p.Text(label, p.Label, RoleColor(p, role), new RectangleF(chart.Right - w - 5 * d, ly, w, p.LineHeight(p.Label)), HAlign.Left, VAlign.Center, false);
        }
        float y = parts[1].Y;
        if (lines == 0)
        {
            p.Text(Nobody(v), p.Small, p.P.Faint, new RectangleF(area.X, y, area.Width, footH), HAlign.Center, VAlign.Center);
            return;
        }
        foreach (var role in Roles)
        {
            if (!Shown(role)) continue;
            var s = Of(role);
            bool ahead = role == DuelRole.Ahead;
            string num = Number(f, s.Car);
            var c = RoleColor(p, role);
            double closing = s.Closing(role), gap = Math.Abs(s.Gap);
            if (s.TrendLaps == 0)
            {
                Run(num, p.SmallBold, c);
                Run("trend after one full lap", p.Small, p.P.Faint);
                FootLine(p, new RectangleF(area.X, y, area.Width, footH), role);
                Run("", p.Small, p.P.Faint);
            }
            else if (closing <= Even)
            {
                Run(num, p.SmallBold, c);
                Run(ahead ? "not catching at this pace" : "not catching you at this pace", p.Small, p.P.Muted);
                FootLine(p, new RectangleF(area.X, y, area.Width, footH), role);
                TrendRuns(p, s, role, false);
            }
            else
            {
                int need = (int)Math.Ceiling(gap / closing);
                if (left < 0)
                {
                    Run(num, p.SmallBold, c);
                    Run(PooledText.Concat(ahead ? "caught in ≈" : "reaches you in ≈", RaceFormat.Count(need, "lap")), p.Small, p.P.Muted);
                    FootLine(p, new RectangleF(area.X, y, area.Width, footH), role);
                    TrendRuns(p, s, role, false);
                }
                else if (need <= left)
                {
                    if (ahead) Run("Catch", p.SmallBold, p.P.GoodText);
                    Run(num, p.SmallBold, ahead ? p.P.GoodText : c);
                    Run(ahead ? PooledText.Number("on lap ", lap + need) : PooledText.Number("reaches you on lap ", lap + need), p.SmallBold, ahead ? p.P.GoodText : p.P.DangerText);
                    FootLine(p, new RectangleF(area.X, y, area.Width, footH), role);
                    TrendRuns(p, s, role, false);
                }
                else
                {
                    Run(num, p.SmallBold, c);
                    Run(PooledText.Concat("needs ", RaceFormat.Count(need, "lap"), PooledText.Number(", ", left, " left")), p.Small, p.P.Muted);
                    FootLine(p, new RectangleF(area.X, y, area.Width, footH), role);
                    double rest = gap - closing * left;
                    Run(ahead ? PooledText.Number("Short by ", rest, "0.0", Inv, " at the flag") : PooledText.Number("You hold them by ", rest, "0.0", Inv), p.SmallBold, ahead ? p.P.DangerText : p.P.GoodText);
                }
            }
            FootLine(p, new RectangleF(area.X, y + footH, area.Width, footH), role);
            y += footH * 2;
        }
    }
}
