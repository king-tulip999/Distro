using System.Drawing;
using System.Drawing.Drawing2D;
using Core.SimRacing.Race;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Hud.Widgets;

internal sealed class StreamCoverWidget : HudWidget
{
    public const string Wordmark = "wordmark", Hatched = "hatched", Card = "card";
    public const double W = 420, H = 220;
    private const string Word = "VELOCITA";

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("style", "Style", Wordmark,
            new HudChoice(Wordmark, "Wordmark", "The Velocita wordmark with session info under it"),
            new HudChoice(Hatched, "Hatched", "A hatched cover with a lock, the session and a clock"),
            new HudChoice(Card, "Driver card", "Your number, license, iRating and best lap")),
    };

    private static readonly string[] Letters = { "V", "E", "L", "O", "C", "I", "T", "A" };
    private static readonly string[] WordmarkParts = { "wordmark", "info" };
    private static readonly string[] SessionCells = { "session", "left", "track" };
    private static readonly string[] CardParts = { "driver", "stats", "footer" };
    private static readonly string[] CardDriver = { "number", "details" };
    private static readonly string[] CardStats = { "best", "laps", "incidents" };

    public override bool HasContent(HudView v) => v.Frame.Status == SimStatus.Connected && v.Frame.IsInGarage;

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v)) return;
        if (!v.Frame.IsInGarage && !p.IsPreview && !v.Frame.Simulated)
        {
            p.Panel();
            p.Text("Shows on iRacing's garage screen", p.Small, p.P.Faint, p.Bounds.Inflated(-10 * p.Density, 0), HAlign.Center, VAlign.Center);
            return;
        }
        var b = p.Bounds;
        float radius = p.Look.Corner > 0 ? Math.Min(p.Look.Corner, 8) : 0;
        p.Fill(b, p.P.Surface, radius);
        var clip = p.ClipEnds(b, radius, true, true);
        switch (v.Options.Choice("style"))
        {
            case Hatched: DrawHatched(p, v, b); break;
            case Card: DrawCard(p, v, b); break;
            default: DrawWordmark(p, v, b); break;
        }
        p.Unclip(clip);
    }

    private static string TimeLeft(HudView v) => Strips.TimeLeft(v) ?? Strips.LapsLeft(v) ?? RaceFormat.Missing;

    private static string TimeLeftLabel(HudView v) => Strips.TimeLeft(v) is null && Strips.LapsLeft(v) is not null ? "Laps left" : "Left";

    private static string Clock(HudView v)
    {
        var f = v.Frame;
        if (f.RawValue("SessionTimeOfDay") is { } seconds && double.IsFinite(seconds) && seconds >= 0)
            return RaceFormat.TimeOfDay(DateTime.Today.AddSeconds(seconds % 86400), v.Settings.Clock24h);
        return RaceFormat.TimeOfDay(DateTime.Now, v.Settings.Clock24h);
    }

    private static string FootSession(HudView v)
    {
        string? time = Strips.TimeLeft(v);
        if (time is not null && v.Frame.SessionTimeRemain is not null) return PooledText.Concat(v.Race.SessionLabel, " · ", PooledText.Concat(time, " left"));
        if (Strips.LapsLeft(v) is { } laps) return PooledText.Concat(v.Race.SessionLabel, " · ", PooledText.Concat(laps, " laps left"));
        return v.Race.SessionLabel;
    }

    private static void SessionTiles(HudPainter p, HudView v, RectangleF r)
    {
        float gap = 4 * p.Density, w = (r.Width - gap * 2) / 3;
        Span<RectangleF> cells = stackalloc RectangleF[3];
        p.Arrange(v, "cover.wordmark.info", r, true, gap, SessionCells, stackalloc float[] { w, w, w }, cells);
        p.BoxedStat(new RectangleF(cells[0].X, r.Y, w, r.Height), "Session", v.Race.SessionLabel, null, p.Bold);
        p.BoxedStat(new RectangleF(cells[1].X, r.Y, w, r.Height), TimeLeftLabel(v), TimeLeft(v), null, p.Bold);
        p.BoxedStat(new RectangleF(cells[2].X, r.Y, w, r.Height), "Track", RaceFormat.Temp(v.Frame.TrackTemp, v.Units), null, p.Bold);
    }

    private static void DrawWordmark(HudPainter p, HudView v, RectangleF b)
    {
        float d = p.Density, pad = 6 * d;
        float tileH = p.BoxedStatHeight(p.Bold);
        p.Need(b.Width, 130 * d + tileH + pad * 2);
        bool showStrip = b.Height - tileH - pad * 2 >= 70 * d;
        float stripH = tileH + pad * 2;
        Span<RectangleF> parts = stackalloc RectangleF[2];
        p.Arrange(v, "cover.wordmark", b, false, 0, WordmarkParts, stackalloc float[] { 0, showStrip ? stripH : -1 }, parts);
        if (showStrip)
        {
            var strip = new RectangleF(parts[1].X, parts[1].Y, parts[1].Width, stripH);
            p.Fill(strip, p.P.Bg);
            float edge = strip.Y > b.Y ? strip.Y : strip.Bottom;
            p.Line(b.X, edge, b.Right, edge, p.P.LineStrong, 1);
            SessionTiles(p, v, strip.Inflated(-pad, -pad));
        }

        var top = parts[0];
        float labelH = p.LineHeight(p.Label), ruleH = 3 * d, gapY = 10 * d;
        bool extras = true;
        float px = Math.Min((top.Height - labelH - ruleH - gapY * 2) * 0.9f, b.Width / 8.5f);
        if (px < 24 * d) { extras = false; px = Math.Min(top.Height * 0.9f, b.Width / 8.5f); }
        if (px < 10) return;
        float tracking = px * 0.32f, width = 0, inkH = 0;
        foreach (var letter in Letters)
        {
            var size = p.DisplaySize(letter, px);
            width += size.Width;
            inkH = Math.Max(inkH, size.Height);
        }
        width += tracking * (Letters.Length - 1);
        float block = extras ? inkH + gapY + ruleH + gapY + labelH : inkH;
        float y = top.Y + (top.Height - block) / 2, x = b.X + (b.Width - width) / 2;
        foreach (var letter in Letters)
        {
            var size = p.DisplaySize(letter, px);
            p.Display(letter, px, p.P.Heading, new RectangleF(x, y, size.Width, inkH), HAlign.Left, VAlign.Bottom);
            x += size.Width + tracking;
        }
        if (!extras) return;
        float ruleW = 64 * d;
        p.Fill(new RectangleF(b.X + (b.Width - ruleW) / 2, y + inkH + gapY, ruleW, ruleH), p.P.Accent);
        p.Text(p.LabelText("Setup hidden"), p.Label, p.P.Muted, new RectangleF(b.X, y + inkH + gapY * 2 + ruleH, b.Width, labelH), HAlign.Center, VAlign.Center);
    }

    private static void DrawHatched(HudPainter p, HudView v, RectangleF b)
    {
        float d = p.Density;
        var hatch = HudPalette.Alpha(p.P.LineStrong, 150);
        var state = p.G.Save();
        p.G.SetClip(b, CombineMode.Intersect);
        float step = 12 * d;
        for (float x = b.X - b.Height; x < b.Right; x += step) p.Line(x, b.Bottom, x + b.Height, b.Y, hatch, 2 * d);
        p.G.Restore(state);

        float pad = 10 * d;
        var chipFont = p.Label;
        string live = p.LabelText("Live");
        float cw = p.Measure(live, chipFont) + 12 * d, ch = p.LineHeight(chipFont) + 4 * d;
        p.Pill(new RectangleF(b.X + pad, b.Y + pad, cw, ch), live, p.P.Accent, chipFont, p.P.OnAccent);
        string clock = Clock(v);
        float clockW = p.Measure(clock, p.Small) + 10 * d;
        var clockBox = new RectangleF(b.Right - pad - clockW, b.Y + pad, clockW, ch);
        p.Fill(clockBox, p.P.Surface, p.CornerRadius(4));
        p.Text(clock, p.Small, p.P.Muted, clockBox, HAlign.Center, VAlign.Center, false);

        var title = p.Font(19f, true);
        float titleH = p.LineHeight(title), subH = p.LineHeight(p.Small), gap = 8 * d;
        float room = b.Height - pad * 2 - ch * 2;
        float badge = Math.Clamp(room - titleH - subH - gap * 2, 0, 58 * d);
        p.Need(b.Width, pad * 2 + ch * 2 + 36 * d + titleH + subH + gap * 2);
        float block = badge + (badge > 0 ? gap : 0) + titleH + 2 * d + subH;
        float y = b.Y + (b.Height - block) / 2;
        if (badge >= 24 * d)
        {
            float cx = b.X + b.Width / 2, cy = y + badge / 2, r = badge / 2;
            p.Dot(cx, cy, r, p.P.Bg);
            p.Outline(new RectangleF(cx - r + d, cy - r + d, r * 2 - 2 * d, r * 2 - 2 * d), p.P.Accent, 2 * d, r);
            Lock(p, cx, cy, badge * 0.62f, p.P.Heading);
            y += badge + gap;
        }
        var titleBox = new RectangleF(b.X + pad, y, b.Width - pad * 2, titleH);
        float tw = Math.Min(titleBox.Width, p.Measure("Setup hidden", title) + 16 * d);
        p.Fill(new RectangleF(b.X + (b.Width - tw) / 2, y - 2 * d, tw, titleH + subH + 6 * d), HudPalette.Alpha(p.P.Surface, 225), p.CornerRadius(4));
        p.Text("Setup hidden", title, p.P.Heading, titleBox, HAlign.Center, VAlign.Center);
        string sub = PooledText.Concat(v.Race.SessionLabel, " · ", TimeLeft(v));
        p.Text(sub, p.Small, p.P.Muted, new RectangleF(b.X + pad, y + titleH + 2 * d, b.Width - pad * 2, subH), HAlign.Center, VAlign.Center);
    }

    private static void Lock(HudPainter p, float cx, float cy, float size, Color c)
    {
        float stroke = Math.Max(1.5f, size * 0.09f);
        float bodyW = size * 0.66f, bodyH = size * 0.5f;
        var body = new RectangleF(cx - bodyW / 2, cy - bodyH / 2 + size * 0.16f, bodyW, bodyH);
        float shackleW = bodyW * 0.62f, shackleH = size * 0.42f;
        float sx = cx - shackleW / 2;
        p.G.DrawArc(p.Pen(c, stroke), sx, body.Y - shackleH, shackleW, shackleW, 180, 180);
        p.Line(sx, body.Y - shackleH + shackleW / 2, sx, body.Y, c, stroke);
        p.Line(sx + shackleW, body.Y - shackleH + shackleW / 2, sx + shackleW, body.Y, c, stroke);
        p.Fill(body, c, p.CornerRadius(4) > 0 ? 2 : 0);
        float hole = Math.Max(2, size * 0.1f);
        p.Dot(cx, body.Y + bodyH * 0.45f, hole / 2 + 0.5f, p.P.Bg);
    }

    private static void DrawCard(HudPainter p, HudView v, RectangleF b)
    {
        float d = p.Density, pad = 6 * d, gap = 6 * d;
        var f = v.Frame;
        int me = f.PlayerCarIdx;
        var driver = f.Session.Player;
        var inner = b.Inflated(-pad, -pad);
        float tileH = p.BoxedStatHeight(p.Bold), footH = p.LineHeight(p.Small) + 8 * d;
        p.Need(b.Width, pad * 2 + 86 * d + gap + tileH + gap + footH);
        float headMin = 54 * d;
        bool showFoot = inner.Height - tileH - gap - footH + pad >= headMin;
        bool showTiles = inner.Height - (showFoot ? footH - pad : 0) - tileH - gap >= headMin;
        Span<RectangleF> parts = stackalloc RectangleF[3];
        p.Arrange(v, "cover.card", RectangleF.FromLTRB(b.X, inner.Y, b.Right, b.Bottom), false, gap, CardParts, stackalloc float[] { 0, showTiles ? tileH : -1, showFoot ? footH : -1 }, parts);
        var foot = new RectangleF(b.X, parts[2].Y, b.Width, footH);
        var tiles = new RectangleF(inner.X, parts[1].Y, inner.Width, tileH);
        var head = new RectangleF(inner.X, parts[0].Y, inner.Width, parts[0].Height);

        var classColor = driver is { } dc ? p.ClassColor(f.Session, dc.ClassId) : p.P.Accent;
        float numW = Math.Min(Math.Max(86 * d, head.Height * 0.9f), head.Width * 0.3f);
        Span<RectangleF> sides = stackalloc RectangleF[2];
        p.Arrange(v, "cover.card.driver", head, true, gap, CardDriver, stackalloc float[] { numW, 0 }, sides);
        var numBox = new RectangleF(sides[0].X, head.Y, numW, head.Height);
        p.Fill(numBox, classColor, p.CornerRadius(4));
        var ink = HudPalette.On(classColor);
        string number = driver is { CarNumber.Length: > 0 } dn ? dn.CarNumber : RaceFormat.Missing;
        string cls = driver?.ClassLabel ?? "";
        float labelH = cls.Length > 0 && f.Session.NumCarClasses > 1 ? p.LineHeight(p.Label) : 0;
        float numPx = Math.Min((numBox.Height - labelH - 10 * d) * 0.95f, 40 * d);
        if (numPx > 8)
        {
            var size = p.DisplaySize(number, numPx);
            if (size.Width > numBox.Width - 10 * d) numPx *= (numBox.Width - 10 * d) / size.Width;
            size = p.DisplaySize(number, numPx);
            float blockH = size.Height + (labelH > 0 ? 4 * d + labelH : 0);
            float ny = numBox.Y + (numBox.Height - blockH) / 2;
            p.Display(number, numPx, ink, new RectangleF(numBox.X, ny, numBox.Width, size.Height), HAlign.Center, VAlign.Top);
            if (labelH > 0) p.Text(p.LabelText(cls), p.Label, HudPalette.Alpha(ink, 220), new RectangleF(numBox.X, ny + size.Height + 4 * d, numBox.Width, labelH), HAlign.Center, VAlign.Center);
        }

        var card = new RectangleF(sides[1].X, head.Y, sides[1].Width, head.Height);
        p.Tile(card);
        var text = card.Inflated(-10 * d, -6 * d);
        float badgeH = p.LineHeight(p.Tiny) + 8 * d, carH = p.LineHeight(p.Small);
        var nameFont = p.Fitting("Ag", 9999, text.Height - badgeH - carH - 4 * d, p.Big, p.Bold, p.SmallBold);
        float nameH = p.LineHeight(nameFont);
        float stack = nameH + badgeH + carH + 4 * d;
        float y = text.Y + Math.Max(0, (text.Height - stack) / 2);
        string name = driver is { } dname ? RaceFormat.Name(dname.Name.Length > 0 ? dname.Name : dname.TeamName, "full") : RaceFormat.Missing;
        p.Text(name, nameFont, p.P.Heading, new RectangleF(text.X, y, text.Width, nameH), HAlign.Left, VAlign.Center);
        y += nameH + 2 * d;
        if (driver is { } dl && y + badgeH <= text.Bottom + 1)
        {
            HudTable.Badge(p, new RectangleF(text.X, y, text.Width, badgeH), dl.License, dl.IRating, "both", false);
            y += badgeH + 2 * d;
        }
        if (y + carH <= text.Bottom + 1)
        {
            string car = driver is { CarName.Length: > 0 } dcar ? dcar.CarName : "";
            string track = f.Session.TrackDisplayName;
            string line = car.Length > 0 && track.Length > 0 ? PooledText.Concat(car, " · ", track) : car.Length > 0 ? car : track;
            p.Text(line, p.Small, p.P.Muted, new RectangleF(text.X, y, text.Width, carH), HAlign.Left, VAlign.Center);
        }

        if (!showTiles) return;
        float tw = (tiles.Width - 8 * d) / 3;
        Span<RectangleF> cells = stackalloc RectangleF[3];
        p.Arrange(v, "cover.card.stats", tiles, true, 4 * d, CardStats, stackalloc float[] { tw, tw, tw }, cells);
        double? best = (uint)me < TelemetryFrame.MaxCars && f.CarBestLap[me] > 0 ? f.CarBestLap[me] : f.LapBest is > 0 ? f.LapBest : null;
        p.BoxedStat(new RectangleF(cells[0].X, tiles.Y, tw, tiles.Height), "Best lap", RaceFormat.LapTime(best), best is null ? p.P.Muted : p.P.Fastest, p.Bold);
        string laps = f.LapCompleted is { } lc && lc >= 0 ? PooledText.Number("", lc) : RaceFormat.Missing;
        p.BoxedStat(new RectangleF(cells[1].X, tiles.Y, tw, tiles.Height), "Laps", laps, null, p.Bold);
        string inc = f.Incidents is { } i ? PooledText.Number("", i, "x") : RaceFormat.Missing;
        p.BoxedStat(new RectangleF(cells[2].X, tiles.Y, tw, tiles.Height), "Incidents", inc, null, p.Bold);

        if (!showFoot) return;
        p.Fill(foot, p.P.WarnSoft);
        var footText = foot.Inflated(-10 * d, 0);
        p.Text("In the garage", p.Small, p.P.WarnText, footText, HAlign.Left, VAlign.Center);
        p.Text(FootSession(v), p.Small, p.P.WarnText, footText, HAlign.Right, VAlign.Center);
    }
}
