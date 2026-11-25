using System.Drawing;
using Core.SimRacing.Race;

namespace Core.SimRacing.Hud.Widgets;

internal sealed class IncidentsWidget : HudWidget
{
    public const string Budget = "budget", Chips = "chips", Line = "line", Timeline = "timeline";
    private const double RowEntrance = 0.35, Flash = 1.4, CellEntrance = 0.4, PinEntrance = 0.45, RingEase = 0.6;
    private const int BudgetRows = 3;

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("style", "Style", Budget,
            new HudChoice(Budget, "Budget bar", "Your total against the limit, then the last three incidents"),
            new HudChoice(Chips, "Chips", "Newest incident large, older ones condensed"),
            new HudChoice(Line, "Single line", "Total, last incident and a count of each kind in one strip"),
            new HudChoice(Timeline, "Session timeline", "Incidents pinned on the race by lap")),
        TitleRow,
        HudOption.Int("count", "Incidents listed", 5, 2, 12),
    };

    private static readonly string[] Names = { "", "Off track", "Loss of control", "", "Car contact" };

    internal static string Name(int points) => points switch
    {
        1 or 2 or 4 => Names[points],
        _ => RaceFormat.Count(points, "incident point"),
    };

    private double _ringFrom, _ringTo, _ringAt = double.NegativeInfinity;

    public override void Draw(HudPainter p, HudView v)
    {
        string style = v.Options.Choice("style");
        if (style == Line)
        {
            if (DrawWaiting(p, v)) return;
            DrawLine(p, v);
            return;
        }
        if (DrawWaiting(p, v, "Incident log")) return;
        switch (style)
        {
            case Chips: DrawChips(p, v); break;
            case Timeline: DrawTimeline(p, v); break;
            default: DrawBudget(p, v); break;
        }
    }

    private static int Total(HudView v) => v.Frame.Incidents ?? 0;

    private static int? Limit(HudView v) => v.Frame.Session.IncidentLimit is > 0 and var l ? l : null;

    private static (Color Fill, Color Text) Kind(HudPainter p, int points) =>
        points >= 4 ? (p.P.Danger, p.P.DangerText) : points >= 2 ? (p.P.Warn, p.P.WarnText) : (p.P.Good, p.P.GoodText);

    private static Color TotalColor(HudPainter p, HudView v) => IncidentBudget.State(Total(v), Limit(v)) switch
    {
        BudgetState.Over => p.P.DangerText,
        BudgetState.Warn => p.P.WarnText,
        _ => p.P.GoodText,
    };

    private static string TotalText(HudView v) =>
        v.Frame.Incidents is not { } inc ? RaceFormat.Missing : Limit(v) is { } lim ? PooledText.Number("", inc, "/", lim, "x") : PooledText.Number("", inc, "x");

    private static double Age(HudView v, IncidentRecord e) => v.Frame.SessionTime is { } now ? Math.Max(0, now - e.SessionTime) : double.PositiveInfinity;

    private static float Ease(HudPainter p, double age, double seconds)
    {
        if (p.ReducedMotion || age >= seconds) return 1;
        double x = Math.Clamp(age / seconds, 0, 1);
        return (float)(1 - Math.Pow(1 - x, 3));
    }

    private static float Pop(HudPainter p, double age)
    {
        if (p.ReducedMotion || age >= PinEntrance) return 1;
        double x = Math.Clamp(age / PinEntrance, 0, 1);
        return (float)(x < 0.7 ? 0.3 + 0.85 * (x / 0.7) : 1.15 - 0.15 * ((x - 0.7) / 0.3));
    }

    private static void Flashed(HudPainter p, HudView v, IncidentRecord e, RectangleF r)
    {
        double age = Age(v, e);
        if (p.ReducedMotion || age >= Flash) return;
        p.Fill(r, HudPalette.Alpha(p.P.Heading, (int)(26 * (1 - age / Flash))));
    }

    private static float Slide(HudPainter p, HudView v, IncidentRecord e) => (1 - Ease(p, Age(v, e), RowEntrance)) * -6 * p.Density;

    private static string Where(IncidentRecord e) =>
        e.Lap is { } lap ? PooledText.Concat(PooledText.Number("L", lap, " · S", e.Sector + 1, " · "), RaceFormat.Clock(e.SessionTime)) : PooledText.Concat(PooledText.Number("S", e.Sector + 1, " · "), RaceFormat.Clock(e.SessionTime));

    private static string LapText(IncidentRecord e) => e.Lap is { } lap ? PooledText.Number("L", lap) : RaceFormat.Missing;

    private static void Empty(HudPainter p, RectangleF r) => p.Text("No incidents", p.Small, p.P.Faint, r, HAlign.Center, VAlign.Center);

    private static float RowHeight(HudPainter p) => 10 * p.Density + p.LineHeight(p.Font(13f, true));

    private static readonly string[] BudgetParts = { "total", "cells", "list" };

    private void DrawBudget(HudPainter p, HudView v)
    {
        float d = p.Density;
        var area = p.Header("Incidents");
        var list = v.Race.Incidents;
        int total = Total(v);
        int? limit = Limit(v);
        float bigPx = 40f, bigH = p.DisplaySize("0", bigPx).Height, cellH = 14 * d, rowH = RowHeight(p);
        p.NeedBelow(area, 4 * d + bigH + 10 * d + cellH + 12 * d + rowH * BudgetRows);
        Span<RectangleF> boxes = stackalloc RectangleF[3];
        p.Arrange(v, "incidents.budget", area, false, 0, BudgetParts, stackalloc float[] { 4 * d + bigH + 10 * d, cellH + 12 * d, 0 }, boxes);
        float top = boxes[0].Y + 4 * d;

        var state = IncidentBudget.State(total, limit);
        string big = v.Frame.Incidents is { } inc ? PooledText.Number("", inc, "x") : RaceFormat.Missing;
        float bigW = p.DisplaySize(big, bigPx).Width;
        var bigColor = state switch { BudgetState.Over => p.P.DangerText, BudgetState.Warn => p.P.WarnText, _ => p.P.Heading };
        p.Display(big, bigPx, bigColor, new RectangleF(area.X, top, bigW + 2, bigH), HAlign.Left, VAlign.Top);
        var ofFont = p.Font(16f, true);
        var sideFont = p.Font(13f, true);
        float baseY = top + bigH;
        float x = area.X + bigW + 10 * d;
        if (limit is { } lim)
        {
            string of = PooledText.Number("of ", lim, "x");
            float ow = p.Measure(of, ofFont);
            float ofCap = ofFont.Size * 0.72f;
            p.Text(of, ofFont, p.P.Muted, new RectangleF(x, baseY - ofCap / 2 - p.LineHeight(ofFont) / 2, ow + 2, p.LineHeight(ofFont)), HAlign.Left, VAlign.Center, false);
            x += ow + 10 * d;
        }
        string side = IncidentBudget.SideText(total, limit);
        float sideCap = sideFont.Size * 0.72f;
        p.Text(side, sideFont, state == BudgetState.Over ? p.P.DangerText : p.P.Muted, new RectangleF(x, baseY - sideCap / 2 - p.LineHeight(sideFont) / 2, Math.Max(0, area.Right - x), p.LineHeight(sideFont)), HAlign.Right, VAlign.Center);

        DrawCells(p, v, list, total, limit, new RectangleF(area.X, boxes[1].Y, area.Width, cellH));

        var rows = boxes[2];
        float y = rows.Y;
        var font = p.Font(13f, true);
        if (list.Count == 0) { Empty(p, new RectangleF(area.X, y, area.Width, Math.Max(rowH, rows.Bottom - y))); return; }
        float gap = 8 * d, timeW = 52 * d, lapW = 34 * d;
        var clip = p.G.Save();
        p.G.SetClip(new RectangleF(area.X, y, area.Width, Math.Max(0, rows.Bottom - y)));
        for (int n = 0, i = list.Count - 1; i >= 0 && n < BudgetRows; i--, n++)
        {
            if (y + rowH > rows.Bottom + 1) break;
            var e = list[i];
            var r = new RectangleF(area.X, y + Slide(p, v, e), area.Width, rowH);
            p.Line(area.X, MathF.Round(y) + 0.5f, area.Right, MathF.Round(y) + 0.5f, p.P.Line, 1);
            Flashed(p, v, e, r);
            string pts = PooledText.Number("", e.Points, "x");
            float pw = p.Measure(pts, font);
            p.Text(RaceFormat.Clock(e.SessionTime), font, p.P.Muted, new RectangleF(r.X, r.Y, timeW, rowH), HAlign.Left, VAlign.Center, false);
            p.Text(LapText(e), font, p.P.Text, new RectangleF(r.X + timeW + gap, r.Y, lapW, rowH), HAlign.Left, VAlign.Center, false);
            float nx = r.X + timeW + gap + lapW + gap;
            p.Text(Name(e.Points), font, p.P.Text, new RectangleF(nx, r.Y, Math.Max(0, r.Right - pw - gap - nx), rowH), HAlign.Left, VAlign.Center);
            p.Text(pts, font, Kind(p, e.Points).Text, new RectangleF(r.Right - pw - 1, r.Y, pw + 1, rowH), HAlign.Right, VAlign.Center, false);
            y += rowH;
        }
        p.G.Restore(clip);
    }

    private void DrawCells(HudPainter p, HudView v, IReadOnlyList<IncidentRecord> list, int total, int? limit, RectangleF bar)
    {
        float d = p.Density, gap = 3 * d, radius = p.Look.Corner > 0 ? 2 * d : 0;
        var w = IncidentBudget.Window(total, limit);
        float cw = Math.Max(1, (bar.Width - gap * (w.Size - 1)) / w.Size), step = cw + gap;
        var empty = HudPalette.Alpha(p.P.Text, 15);
        var warnEdge = HudPalette.Alpha(p.P.Warn, 89);
        var older = HudPalette.Alpha(p.P.Muted, 150);
        int listed = 0;
        foreach (var e in list) listed += e.Points;
        int unknown = Math.Max(0, total - listed);
        float shift = 0, grow = 1;
        int newestStart = -1;
        if (list.Count > 0)
        {
            var newest = list[^1];
            float k = Ease(p, Age(v, newest), CellEntrance);
            newestStart = total - newest.Points;
            grow = k;
            if (w.First > 0) shift = Math.Min(newest.Points, w.First) * (1 - k);
        }
        int from = Math.Max(0, w.First - (int)Math.Ceiling(shift));
        float X(int point) => bar.X + (point - w.First + shift) * step;
        var clip = p.G.Save();
        p.G.SetClip(new RectangleF(bar.X, bar.Y - 3 * d, bar.Width, bar.Height + 6 * d));
        int cursor = 0, cursorEnd = list.Count > 0 ? list[0].Points : 0;
        for (int point = from; point < w.First + w.Size; point++)
        {
            float x = X(point);
            var cell = new RectangleF(x, bar.Y, cw, bar.Height);
            Color c;
            if (point >= total) c = empty;
            else if (point < unknown) c = older;
            else
            {
                int listedPoint = point - unknown;
                while (cursor < list.Count - 1 && listedPoint >= cursorEnd) cursorEnd += list[++cursor].Points;
                c = Kind(p, list[cursor].Points).Fill;
            }
            if (point < total && point >= newestStart && newestStart >= 0) cell.Width = Math.Max(0.5f, cw * grow);
            int fromLeft = point - w.First;
            if (w.First > 0 && fromLeft < 2) c = HudPalette.Alpha(c, (int)(c.A * (fromLeft <= 0 ? 0.35f : 0.7f)));
            p.Fill(cell, c, radius);
            if (w.WarnFrom is { } wf && w.Limit is { } lim && point >= wf && point < lim)
                p.Fill(new RectangleF(x, bar.Bottom - 2 * d, cw, 2 * d), warnEdge);
        }
        if (w.Limit is { } l && total > l)
        {
            float mx = X(l) - gap / 2;
            if (mx > bar.X + 1 && mx < bar.Right - 1) p.Fill(new RectangleF(mx - d, bar.Y - 2 * d, 2 * d, bar.Height + 4 * d), p.P.Heading, d);
        }
        p.G.Restore(clip);
    }

    private static readonly string[] ChipsParts = { "newest", "older" };

    private void DrawChips(HudPainter p, HudView v)
    {
        float d = p.Density;
        var area = p.Header("Incident log", TotalText(v), TotalColor(p, v));
        var list = v.Race.Incidents;
        int count = v.Options.Int("count");
        float bigH = 40 * d, smallH = 28 * d, gap = 6 * d;
        p.NeedBelow(area, 6 * d + bigH + (count - 1) * (smallH + gap));
        Span<RectangleF> boxes = stackalloc RectangleF[2];
        p.Arrange(v, "incidents.chips", area, false, 0, ChipsParts, stackalloc float[] { 6 * d + bigH + gap, 0 }, boxes);
        bool newestFirst = v.Options.Arranged("incidents.chips", ChipsParts)[0] == 0;
        float y = area.Y + 6 * d;
        if (list.Count == 0) { Empty(p, new RectangleF(area.X, y, area.Width, Math.Max(bigH, area.Bottom - y))); return; }
        float olderBottom = newestFirst ? area.Bottom : boxes[1].Bottom;
        var nameBig = p.Font(14f, true);
        var whereFont = p.Font(13f, true);
        var clip = p.G.Save();
        p.G.SetClip(area);
        for (int n = 0, i = list.Count - 1; i >= 0 && n < count; i--, n++)
        {
            bool first = n == 0;
            float h = first ? bigH : smallH;
            if (first) y = boxes[0].Y + 6 * d;
            else if (n == 1) y = newestFirst ? boxes[0].Y + 6 * d + (bigH + gap) : boxes[1].Y;
            if (y + h > (first ? area.Bottom : olderBottom) + 1) break;
            var e = list[i];
            var r = new RectangleF(area.X, y + Slide(p, v, e), area.Width, h);
            if (first) p.Fill(r, p.Solid(p.Raised(0.03)), p.Look.Corner > 0 ? 8 * d : 0);
            Flashed(p, v, e, r);
            var (fill, _) = Kind(p, e.Points);
            float bw = (first ? 34 : 30) * d, bh = (first ? 28 : 22) * d, bx = r.X + 6 * d;
            var badge = new RectangleF(bx, r.Y + (h - bh) / 2, bw, bh);
            p.Fill(badge, fill, p.Look.Corner > 0 ? 6 * d : 0);
            string pts = PooledText.Number("", e.Points, "x");
            p.Display(pts, first ? 17f : 14f, p.P.Bg, badge, HAlign.Center, VAlign.Center);
            string where = Where(e);
            float ww = p.Measure(where, whereFont);
            float nx = badge.Right + 10 * d, right = r.Right - 10 * d;
            p.Text(where, whereFont, p.P.Muted, new RectangleF(right - ww, r.Y, ww + 1, h), HAlign.Right, VAlign.Center, false);
            p.Text(Name(e.Points), first ? nameBig : whereFont, first ? p.P.Heading : p.P.Text, new RectangleF(nx, r.Y, Math.Max(0, right - ww - 10 * d - nx), h), HAlign.Left, VAlign.Center);
            y += h + gap;
        }
        p.G.Restore(clip);
    }

    private static readonly string[] LineParts = { "ring", "last", "counts" };

    private void DrawLine(HudPainter p, HudView v)
    {
        float d = p.Density, ring = 56 * d, pad = 12 * d;
        p.Need(p.Bounds.Width, ring + 8 * d + pad * 2);
        p.Panel();
        var area = p.Bounds.Inflated(-14 * d, -pad);
        var list = v.Race.Incidents;
        int total = Total(v);
        int? limit = Limit(v);
        var state = IncidentBudget.State(total, limit);
        float cy = area.Y + area.Height / 2;
        const float numFont = 20f;
        var unitFont = p.Font(13f, true);
        float colW = Math.Max(p.DisplaySize("000", numFont).Width, p.Measure("4x", unitFont)) + 2 * d;
        float countsW = colW * 3 + 10 * d * 2;
        Span<RectangleF> boxes = stackalloc RectangleF[3];
        p.Arrange(v, "incidents.line", area, true, 0, LineParts, stackalloc float[] { 2 * d + ring + 14 * d, 0, 14 * d + countsW }, boxes);
        float ringX = boxes[0].X;
        var ringBox = new RectangleF(ringX + 2 * d + 2.5f * d, cy - ring / 2 + 2.5f * d, ring - 5 * d, ring - 5 * d);
        p.G.DrawEllipse(p.Pen(HudPalette.Alpha(p.P.Text, 20), 5 * d), ringBox);
        double target = limit is { } lim ? Math.Min(1, total / (double)lim) : total > 0 ? 1 : 0;
        if (double.IsNegativeInfinity(_ringAt))
        {
            _ringFrom = _ringTo = target;
            _ringAt = v.Now;
        }
        else if (Math.Abs(target - _ringTo) > 1e-9)
        {
            _ringFrom = RingShown(v.Now);
            _ringTo = target;
            _ringAt = v.Now;
        }
        double shown = p.ReducedMotion ? target : RingShown(v.Now);
        var ringColor = state switch { BudgetState.Over => p.P.Danger, BudgetState.Warn => p.P.Warn, _ => p.P.Good };
        if (shown > 0.002) p.G.DrawArc(p.Pen(ringColor, 5 * d), ringBox, -90, (float)(360 * shown));
        p.Display(v.Frame.Incidents is { } inc ? PooledText.Number("", inc) : RaceFormat.Missing, 17f, p.P.Heading, new RectangleF(ringX + 2 * d, cy - ring / 2, ring, ring), HAlign.Center, VAlign.Center);

        float countsX = boxes[2].X + 14 * d;
        float numH = p.DisplaySize("0", numFont).Height, unitH = p.LineHeight(unitFont), stack = numH + 3 * d + unitH;
        Span<int> kinds = stackalloc[] { 1, 2, 4 };
        for (int k = 0; k < 3; k++)
        {
            int n = 0;
            foreach (var e in list) if (e.Points == kinds[k]) n++;
            float cx = countsX + k * (colW + 10 * d);
            p.Display(PooledText.Number("", n), numFont, Kind(p, kinds[k]).Text, new RectangleF(cx, cy - stack / 2, colW, numH), HAlign.Center, VAlign.Top);
            p.Text(PooledText.Number("", kinds[k], "x"), unitFont, p.P.Muted, new RectangleF(cx, cy - stack / 2 + numH + 3 * d, colW, unitH), HAlign.Center, VAlign.Top, false);
        }

        float lx = boxes[1].X;
        float lineRight = boxes[1].Right;
        p.Line(MathF.Round(lx) + 0.5f, cy - ring / 2 + 4 * d, MathF.Round(lx) + 0.5f, cy + ring / 2 - 4 * d, p.P.LineStrong, 1);
        float tx = lx + 14 * d, tw = Math.Max(0, lineRight - tx);
        var kFont = p.Font(11f, true);
        var nameFont = p.Font(17f, true);
        var whereFont = p.Font(13f, true);
        float block = p.LineHeight(kFont) + 2 * d + p.LineHeight(nameFont) + 2 * d + p.LineHeight(whereFont);
        float ty = cy - block / 2;
        IncidentRecord? last = list.Count > 0 ? list[^1] : null;
        float slide = last is { } le ? Slide(p, v, le) : 0;
        ty += slide;
        string head = PooledText.Concat("LAST · ", IncidentBudget.SideText(total, limit, true));
        p.Text(head, kFont, state == BudgetState.Over ? p.P.DangerText : p.TitleColor, new RectangleF(tx, ty, tw, p.LineHeight(kFont)), HAlign.Left, VAlign.Top);
        ty += p.LineHeight(kFont) + 2 * d;
        if (last is { } e2)
        {
            p.Text(PooledText.Concat(Name(e2.Points), PooledText.Number(" ", e2.Points, "x")), nameFont, Kind(p, e2.Points).Text, new RectangleF(tx, ty, tw, p.LineHeight(nameFont)), HAlign.Left, VAlign.Top);
            ty += p.LineHeight(nameFont) + 2 * d;
            p.Text(Where(e2), whereFont, p.P.Muted, new RectangleF(tx, ty, tw, p.LineHeight(whereFont)), HAlign.Left, VAlign.Top);
        }
        else
        {
            p.Text("No incidents", nameFont, p.P.Heading, new RectangleF(tx, ty, tw, p.LineHeight(nameFont)), HAlign.Left, VAlign.Top);
            ty += p.LineHeight(nameFont) + 2 * d;
            p.Text(RaceFormat.Missing, whereFont, p.P.Muted, new RectangleF(tx, ty, tw, p.LineHeight(whereFont)), HAlign.Left, VAlign.Top);
        }
    }

    private double RingShown(double now)
    {
        double x = Math.Clamp((now - _ringAt) / RingEase, 0, 1);
        double k = 1 - Math.Pow(1 - x, 3);
        return _ringFrom + (_ringTo - _ringFrom) * k;
    }

    private static readonly string[] TimelineParts = { "timeline", "summary" };

    private void DrawTimeline(HudPainter p, HudView v)
    {
        float d = p.Density;
        var f = v.Frame;
        var area = p.Header("Incident log", TotalText(v), TotalColor(p, v));
        var list = v.Race.Incidents;
        var sumFont = p.Font(13f, true);
        float tlH = 96 * d, sumH = 8 * d + p.LineHeight(sumFont);
        p.NeedBelow(area, 8 * d + tlH + 4 * d + sumH);
        double now = (f.LapCompleted ?? 0) + Math.Clamp(f.LapDistPct ?? 0, 0, 1);
        double? avg = LapMath.Stats(v.Race.Laps.Laps, 5)?.Average;
        double span = IncidentBudget.Span(f.CurrentSession?.Laps, f.SessionTimeTotal, avg, now);
        int sectors = Math.Max(1, v.Race.Laps.SectorCount);
        Span<RectangleF> boxes = stackalloc RectangleF[2];
        p.Arrange(v, "incidents.timeline", area, false, 0, TimelineParts, stackalloc float[] { 8 * d + tlH + 4 * d, 0 }, boxes);
        var tl = new RectangleF(area.X + 4 * d, boxes[0].Y + 8 * d, area.Width - 8 * d, tlH);
        float axisY = tl.Y + 62 * d;
        var (start, length) = IncidentBudget.TimelineWindow(span, now);
        float X(double laps) => tl.X + (float)((laps - start) / length * tl.Width);

        p.Fill(new RectangleF(tl.X, axisY, tl.Width, 2 * d), HudPalette.Alpha(p.P.Text, 20), d);
        p.Fill(new RectangleF(tl.X, axisY, Math.Max(0, X(now) - tl.X), 2 * d), HudPalette.Alpha(p.P.Text, 64), d);
        var clip = p.G.Save();
        p.G.SetClip(new RectangleF(tl.X - 4 * d, tl.Y, tl.Width + 8 * d, tl.Height));
        var tickFont = p.Font(11f, true);
        int step = IncidentBudget.TickStep(length);
        for (int lap = (int)Math.Ceiling(start / step) * step; lap <= start + length + 1e-9; lap += step)
        {
            float tx = X(lap);
            p.Fill(new RectangleF(tx, axisY - 4 * d, 1, 10 * d), HudPalette.Alpha(p.P.Text, 31));
            string label = lap == 0 ? "Start" : PooledText.Number("L", lap);
            float lw = p.Measure(label, tickFont);
            float lx = Math.Clamp(tx - lw / 2, tl.X - 4 * d, tl.Right + 4 * d - lw);
            p.Text(label, tickFont, p.P.Faint, new RectangleF(lx, axisY + 10 * d, lw + 1, p.LineHeight(tickFont)), HAlign.Left, VAlign.Top, false);
        }
        float labelRight = float.NegativeInfinity;
        foreach (var e in list)
        {
            if (e.Lap is not { } lap) continue;
            double at = IncidentBudget.Position(lap, e.Sector, sectors);
            if (at < start - 0.5 || at > start + length + 0.5) continue;
            float px = X(at), s = Pop(p, Age(v, e));
            var (fill, text) = Kind(p, e.Points);
            float barH = (8 + e.Points * 6) * d * s, barW = 3 * d;
            p.Fill(new RectangleF(px - barW / 2, axisY - barH, barW, barH), fill, d);
            float labelPx = 13f * s;
            string pts = PooledText.Number("", e.Points, "x");
            var size = p.DisplaySize(pts, labelPx);
            float left = px - size.Width / 2 - 1;
            if (left < labelRight + 2 * d) continue;
            labelRight = left + size.Width + 2;
            p.Display(pts, labelPx, text, new RectangleF(left, axisY - barH - 2 * d - size.Height, size.Width + 2, size.Height), HAlign.Center, VAlign.Top);
        }
        float nx = X(now);
        p.Fill(new RectangleF(nx - d, axisY - 8 * d, 2 * d, 18 * d), p.P.Heading, d);
        p.G.Restore(clip);

        float sy = boxes[1].Y;
        p.Line(area.X, MathF.Round(sy) + 0.5f, area.Right, MathF.Round(sy) + 0.5f, p.P.Line, 1);
        var row = new RectangleF(area.X, sy + 8 * d, area.Width, p.LineHeight(sumFont));
        string count = RaceFormat.Count(list.Count, "incident");
        float cw = p.Measure(count, sumFont);
        p.Text(count, sumFont, p.P.Muted, new RectangleF(row.Right - cw - 1, row.Y, cw + 1, row.Height), HAlign.Right, VAlign.Center, false);
        const string lastLabel = "Last ";
        float llw = p.Measure(lastLabel, sumFont);
        p.Text(lastLabel, sumFont, p.P.Muted, new RectangleF(row.X, row.Y, llw + 1, row.Height), HAlign.Left, VAlign.Center, false);
        string what = list.Count > 0 ? LastText(list[^1]) : RaceFormat.Missing;
        p.Text(what, sumFont, p.P.Heading, new RectangleF(row.X + llw, row.Y, Math.Max(0, row.Right - cw - 10 * d - row.X - llw), row.Height), HAlign.Left, VAlign.Center);
    }

    private static string LastText(IncidentRecord e) =>
        e.Lap is { } lap ? PooledText.Concat(Name(e.Points), PooledText.Number(", L", lap, " S", e.Sector + 1)) : PooledText.Concat(Name(e.Points), PooledText.Number(", S", e.Sector + 1));
}

internal enum BudgetState { Clear, Warn, Over }

internal readonly record struct BudgetWindow(int Size, int First, int? Limit, int? WarnFrom);

internal static class IncidentBudget
{
    public const int MaxCells = 40, OpenCells = 20;
    private static readonly int[] NiceSteps = { 1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000 };

    public static BudgetState State(int total, int? limit) =>
        limit is not { } l ? BudgetState.Clear : total > l ? BudgetState.Over : total >= l * 0.75 ? BudgetState.Warn : BudgetState.Clear;

    public static BudgetWindow Window(int total, int? limit)
    {
        int? l = limit is > 0 ? limit : null;
        int size = l is { } lim ? Math.Min(MaxCells, lim) : OpenCells;
        int first = Math.Max(0, total - size);
        return new BudgetWindow(size, first, l, l is { } w ? (int)Math.Ceiling(w * 0.75) : null);
    }

    public static string SideText(int total, int? limit, bool caps = false)
    {
        if (limit is not { } l) return caps ? "NO LIMIT" : "No limit";
        int left = l - total;
        if (left > 0) return PooledText.Number("", left, caps ? "x TO LIMIT" : "x to limit");
        if (left == 0) return caps ? "AT LIMIT" : "At limit";
        return PooledText.Number("", -left, caps ? "x OVER LIMIT" : "x over limit");
    }

    public static double Position(int lap, int sector, int sectors) =>
        Math.Max(0, lap - 1) + (Math.Clamp(sector, 0, Math.Max(0, sectors - 1)) + 0.5) / Math.Max(1, sectors);

    public static double Span(int? sessionLaps, double? sessionSeconds, double? averageLap, double now)
    {
        double span = sessionLaps is > 0 and var laps ? laps
            : sessionSeconds is > 0 and var s && averageLap is > 0 and var a ? Math.Ceiling(s / a)
            : Math.Ceiling(now) + 1;
        return Math.Max(1, Math.Max(span, Math.Ceiling(now)));
    }

    public const int WholeRaceLaps = 12, WindowLaps = 10, WindowLead = 8;

    public static (double Start, double Length) TimelineWindow(double span, double now)
    {
        if (span <= WholeRaceLaps) return (0, Math.Max(1, span));
        double start = Math.Clamp(now - WindowLead, 0, span - WindowLaps);
        return (start, WindowLaps);
    }

    public static int TickStep(double span)
    {
        int raw = Math.Max(1, (int)Math.Ceiling(span / 6));
        foreach (int nice in NiceSteps) if (nice >= raw) return nice;
        return raw;
    }
}
