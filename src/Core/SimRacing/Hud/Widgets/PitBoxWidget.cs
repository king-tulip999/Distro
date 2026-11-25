using System.Drawing;
using System.Globalization;
using Core.Config;
using Units = Core.Services.CapsUnits;
using Core.SimRacing.Race;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Hud.Widgets;

internal sealed class PitBoxWidget : HudWidget
{
    public const string Countdown = "countdown", Lane = "lane", Stop = "stop";
    public const double W = 340, H = 190;
    private const PitServices Tyres = PitServices.LeftFront | PitServices.RightFront | PitServices.LeftRear | PitServices.RightRear;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string SecondsUnit = " " + Units.S;
    private static readonly PitServices[] TyreOrder = { PitServices.LeftFront, PitServices.RightFront, PitServices.LeftRear, PitServices.RightRear };
    private static readonly string[] CountdownParts = { "tiles", "lane", "footer" };
    private static readonly string[] CountdownTiles = { "target", "speed", "limit" };
    private static readonly string[] LaneParts = { "lane", "scale", "stats" };
    private static readonly string[] LaneStats = { "box", "exit", "time" };
    private static readonly string[] StopParts = { "progress", "stats" };
    private static readonly string[] StopProgress = { "ring", "services" };
    private static readonly string[] StopStats = { "rejoin", "ahead", "traffic" };

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("style", "Style", Stop,
            new HudChoice(Countdown, "Countdown", "Meters to your box, your speed against the limit and the lane"),
            new HudChoice(Lane, "Lane drawing", "The pit lane with its ends, the cars in it and your box"),
            new HudChoice(Stop, "Stop progress", "The stop counting down, each service and where you'll rejoin")),
        TitleRow,
    };

    private readonly PitLaneLearner _learner = new();
    private readonly PitFill _fill = new();
    private readonly double[] _otherPct = new double[TelemetryFrame.MaxCars];
    private readonly float[] _otherFrac = new float[TelemetryFrame.MaxCars];
    private readonly Color[] _otherColor = new Color[TelemetryFrame.MaxCars];
    private readonly string[] _otherNumber = new string[TelemetryFrame.MaxCars];
    private readonly float[] _labelRight = new float[2];
    private readonly PointF[] _needle = new PointF[3];
    private int _others;

    private bool _sample, _inStall, _stopActive, _limiter, _entryKnown, _exitKnown, _passedBox, _hasBox;
    private double? _toBox, _toExit, _speedKph, _limitKph, _laneTime;
    private PitServices _services;
    private double? _fuelRequest, _fuelLeft, _fuelProgress, _fuelSeconds, _repairLeft, _repairTotal, _optLeft;
    private float _boxFrac, _meFrac;
    private string _exitPos = RaceFormat.Missing, _aheadNumber = "";
    private Color _exitColor;
    private double? _aheadGap;
    private int? _traffic;

    public override bool HasContent(HudView v)
    {
        _learner.Observe(v.Frame);
        return OnLane(v.Frame);
    }

    private static bool OnLane(TelemetryFrame f) =>
        f.Status == SimStatus.Connected && f.IsOnTrack && !f.IsReplayPlaying && (f.OnPitRoad || f.PlayerSurface == TrackSurface.ApproachingPits);

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Pit box helper")) return;
        var f = v.Frame;
        _learner.Observe(f);
        string style = v.Options.Choice("style");
        bool live = OnLane(f);
        _fill.Update(live && f.InPitStall && f.PitstopActive, f.Fuel, f.PitFuel, Tank(f), f.PitRepairLeft, f.PitOptRepairLeft, f.SessionTime ?? v.Now);
        if (!live && !(p.IsPreview || f.Simulated))
        {
            var quiet = p.Header("Pit box helper");
            p.Text("Shows on pit road", p.Small, p.P.Muted, quiet, HAlign.Center, VAlign.Center);
            return;
        }
        if (live) Gather(p, v);
        else Sample(p, style == Stop);
        switch (style)
        {
            case Countdown: DrawCountdown(p, v); break;
            case Lane: DrawLane(p, v); break;
            default: DrawStop(p, v); break;
        }
    }

    private static double? Tank(TelemetryFrame f) =>
        f.Session.FuelMaxLiters is > 0 and var max ? max * (f.Session.MaxFuelPct is > 0 and var pct ? pct : 1) : null;

    private void Gather(HudPainter p, HudView v)
    {
        var f = v.Frame;
        var s = f.Session;
        _sample = false;
        _inStall = f.InPitStall;
        _stopActive = f.PitstopActive;
        _limiter = (f.Warnings & EngineWarnings.PitLimiter) != 0;
        _speedKph = f.Speed is { } sp ? sp * 3.6 : null;
        _limitKph = s.PitSpeedLimitKph;
        _laneTime = v.Race.PitTimer;
        double? me = f.LapDistPct;
        _hasBox = s.PitBoxPct is not null;
        _toBox = PitLane.MetresTo(me, s.PitBoxPct, s.TrackLengthKm);
        _passedBox = _toBox is < -5;
        _entryKnown = _learner.EntryPct is not null;
        _exitKnown = _learner.ExitPct is not null;
        _toExit = _exitKnown ? PitLane.MetresTo(me, _learner.ExitPct, s.TrackLengthKm) : null;
        if (_toExit is < 0) _toExit = null;

        _others = 0;
        int player = f.PlayerCarIdx;
        for (int i = 0; i < TelemetryFrame.MaxCars; i++)
        {
            if (i == player || !f.CarOnPitRoad[i] || !f.CarActive(i) || f.CarLapDistPct[i] < 0) continue;
            _otherPct[_others] = f.CarLapDistPct[i];
            s.ByCar.TryGetValue(i, out var d);
            _otherColor[_others] = p.ClassColor(s, d?.ClassId ?? 0);
            _otherNumber[_others] = d is { CarNumber.Length: > 0 } ? PooledText.Concat("#", d.CarNumber) : "";
            _others++;
        }
        double center = s.PitBoxPct ?? me ?? 0;
        Span<double> extra = stackalloc double[TelemetryFrame.MaxCars + 1];
        extra[0] = me ?? -1;
        for (int i = 0; i < _others; i++) extra[i + 1] = _otherPct[i];
        var (from, length) = PitLane.Span(center, _learner.EntryPct, _learner.ExitPct, extra[..(_others + 1)]);
        _boxFrac = (float)PitLane.Place(center, from, length);
        _meFrac = me is { } m ? (float)PitLane.Place(m, from, length) : float.NaN;
        for (int i = 0; i < _others; i++) _otherFrac[i] = (float)PitLane.Place(_otherPct[i], from, length);

        _services = f.PitFlags;
        _fuelRequest = (f.PitFlags & PitServices.Fuel) != 0 ? f.PitFuel : null;
        if (_fill.Active)
        {
            _fuelLeft = _fill.LitresLeft;
            _fuelProgress = _fill.Progress;
            _fuelSeconds = _fill.SecondsLeft;
            _repairTotal = _fill.RepairTotal > 0 ? _fill.RepairTotal : null;
        }
        else
        {
            _fuelLeft = _fuelRequest;
            _fuelProgress = null;
            _fuelSeconds = null;
            _repairTotal = null;
        }
        _repairLeft = f.PitRepairLeft is > 0 and var r ? r : null;
        _optLeft = f.PitOptRepairLeft is > 0 and var o ? o : null;

        var x = v.Race.PitExit;
        if (x is { Valid: true })
        {
            _exitPos = PooledText.Number("P", x.ProjectedClassPos);
            if (!x.Calibrated) _exitPos = RaceFormat.About(_exitPos);
            _exitColor = x.Unreliable ? p.P.Faint : p.P.Heading;
            _aheadGap = x.Ahead?.Gap;
            _aheadNumber = x.Ahead is { } a && s.ByCar.TryGetValue(a.CarIdx, out var ad) && ad.CarNumber.Length > 0 ? PooledText.Concat("#", ad.CarNumber) : "";
            _traffic = x.Traffic.Count;
        }
        else
        {
            _exitPos = RaceFormat.Missing;
            _exitColor = p.P.Faint;
            _aheadGap = null;
            _aheadNumber = "";
            _traffic = null;
        }
    }

    private void Sample(HudPainter p, bool stall)
    {
        _sample = true;
        _inStall = _stopActive = stall;
        _limiter = true;
        _speedKph = stall ? 0 : 58;
        _limitKph = 60;
        _laneTime = stall ? 14.6 : 6.8;
        _hasBox = _entryKnown = _exitKnown = true;
        _toBox = stall ? 0 : 84;
        _passedBox = false;
        _toExit = stall ? 228 : 312;
        _boxFrac = 0.62f;
        _meFrac = stall ? 0.62f : 0.38f;
        _others = 2;
        _otherFrac[0] = 0.18f;
        _otherColor[0] = p.P.Attack;
        _otherNumber[0] = "#22";
        _otherFrac[1] = 0.84f;
        _otherColor[1] = p.P.Slate;
        _otherNumber[1] = "#7";
        _services = PitServices.Fuel | Tyres;
        _fuelRequest = 30;
        _fuelLeft = stall ? 12.6 : 30;
        _fuelProgress = stall ? 0.58 : null;
        _fuelSeconds = stall ? 5.3 : null;
        _repairLeft = _repairTotal = _optLeft = null;
        _exitPos = RaceFormat.About("P8");
        _exitColor = p.P.Heading;
        _aheadGap = 1.9;
        _aheadNumber = "#22";
        _traffic = 0;
    }

    private static string Seconds(double s) => PooledText.Number("", s, "0.0", Inv, SecondsUnit);

    private static string Whole(double value) => PooledText.Number("", value, "0", CultureInfo.CurrentCulture);

    private static (string Number, string Unit) Distance(double metres, HudUnits u) =>
        u == HudUnits.Imperial ? (Whole(Math.Max(0, metres) * 3.28084), Units.Ft) : (Whole(Math.Max(0, metres)), Units.M);

    private double? ServiceLeft => _inStall ? Max(_fuelSeconds, _repairLeft) : null;

    private static double? Max(double? a, double? b) => a is { } x ? b is { } y ? Math.Max(x, y) : x : b;

    private string ServiceLabel =>
        _repairLeft is { } r && (_fuelSeconds is not { } fs || r > fs) ? "Repair left" : _fuelRequest is > 0 ? "Fuel left" : "In box";

    private Color SpeedColor(HudPainter p) => _speedKph is { } s && _limitKph is { } l
        ? PitLane.State(s, l) switch { SpeedState.Over => p.P.DangerText, SpeedState.Near => p.P.WarnText, _ => p.P.GoodText }
        : p.P.Heading;

    private string SpeedText(HudView v) => _speedKph is { } s ? Whole(RaceFormat.SpeedValue(s / 3.6, v.SpeedUnits)) : RaceFormat.Missing;

    private string LimitText(HudView v) => _limitKph is { } l ? Whole(RaceFormat.SpeedValue(l / 3.6, v.SpeedUnits)) : RaceFormat.Missing;

    private (string Number, string Unit, string Label) Target(HudView v)
    {
        if (_inStall)
            return ServiceLeft is { } left ? (PooledText.Number("", left, "0.0", Inv), Units.S, ServiceLabel) : (RaceFormat.Missing, "", ServiceLabel);
        if (_toBox is { } box && !_passedBox && _hasBox)
        {
            var (n, u) = Distance(box, v.Units);
            return (n, u, "To box");
        }
        if (_toExit is { } exit)
        {
            var (n, u) = Distance(exit, v.Units);
            return (n, u, "To pit out");
        }
        return _laneTime is { } t ? (PooledText.Number("", t, "0.0", Inv), Units.S, "In lane") : (RaceFormat.Missing, "", "To box");
    }

    private static void Chip(HudPainter p, string text, Color bg, Color fg)
    {
        if (!p.TitleRowShown) return;
        float d = p.Density, h = p.PillHeight - 8 * d;
        string caps = p.LabelText(text);
        float w = p.Measure(caps, p.Label) + 14 * d;
        var r = new RectangleF(p.Bounds.Width - 8 * d - w, (p.PillHeight - h) / 2, w, h);
        p.Fill(r, bg, p.CornerRadius(4));
        p.Text(caps, p.Label, fg, r, HAlign.Center, VAlign.Center, false);
    }

    private void LimiterChip(HudPainter p)
    {
        if (_inStall) return;
        if (_limiter) Chip(p, "Limiter on", p.P.GoodSoft, p.P.GoodText);
        else Chip(p, "Limiter off", p.P.WarnSoft, p.P.WarnText);
    }

    private static void BigTile(HudPainter p, RectangleF r, string number, string unit, string label, Color color, float px)
    {
        float d = p.Density;
        p.Tile(r);
        var font = p.Font(px, true);
        float lh = p.LineHeight(p.Label), vh = p.LineHeight(font), stack = vh + lh;
        float top = r.Y + (r.Height - stack) / 2;
        var inner = r.Inflated(-6 * d, 0);
        if (p.Measure(number, font) + (unit.Length > 0 ? p.Measure(unit, p.SmallBold) + 4 : 0) > inner.Width) font = p.Big;
        p.Number(number, unit, font, color, new RectangleF(inner.X, top, inner.Width, vh), HAlign.Center, VAlign.Bottom);
        p.Text(p.LabelText(label), p.Label, p.LabelColor, new RectangleF(inner.X, top + vh, inner.Width, lh), HAlign.Center, VAlign.Top);
    }

    private void DrawCountdown(HudPainter p, HudView v)
    {
        float d = p.Density, gap = 6 * d;
        var area = p.Header(_inStall ? "In box" : "Pit lane");
        LimiterChip(p);
        float tileH = Math.Min(p.LineHeight(p.Font(26f, true)) + p.LineHeight(p.Label) + 12 * d, area.Height);
        float laneH = 30 * d, footH = p.LineHeight(p.SmallBold) + 8 * d;
        p.NeedBelow(area, tileH + gap + laneH + gap + footH);

        float laneTop = area.Y + tileH + gap;
        bool lane = laneTop + laneH <= area.Bottom + 1;
        float footTop = lane ? laneTop + (laneH + gap) : laneTop;
        bool foot = footTop + footH <= area.Bottom + 1;
        Span<RectangleF> parts = stackalloc RectangleF[3];
        p.Arrange(v, "pitbox.countdown", area, false, gap, CountdownParts, stackalloc float[] { tileH, lane ? laneH : -1, foot ? footH : -1 }, parts);
        var row = new RectangleF(parts[0].X, parts[0].Y, parts[0].Width, tileH);
        float total = row.Width - gap * 2, w1 = total * 1.4f / 3.4f, w2 = total / 3.4f;
        Span<RectangleF> cells = stackalloc RectangleF[3];
        p.Arrange(v, "pitbox.countdown.tiles", row, true, gap, CountdownTiles, stackalloc float[] { w1, w2, w2 }, cells);
        var t1 = new RectangleF(cells[0].X, row.Y, w1, tileH);
        var t2 = new RectangleF(cells[1].X, row.Y, w2, tileH);
        var t3 = new RectangleF(cells[2].X, row.Y, w2, tileH);
        var (num, unit, label) = Target(v);
        BigTile(p, t1, num, unit, label, p.P.Heading, 26f);
        if (!_inStall && _hasBox && !_passedBox && !float.IsNaN(_meFrac) && _boxFrac > 0.01f)
        {
            float k = Math.Clamp(_meFrac / _boxFrac, 0, 1);
            p.Fill(new RectangleF(t1.X, t1.Bottom - 2 * d, t1.Width * k, 2 * d), p.P.Accent);
        }
        BigTile(p, t2, SpeedText(v), "", RaceFormat.SpeedUnit(v.SpeedUnits), SpeedColor(p), 22f);
        p.Fill(new RectangleF(t2.X, t2.Bottom - 2 * d, t2.Width, 2 * d), SpeedFill(p));
        BigTile(p, t3, LimitText(v), "", "Limit", p.P.Heading, 22f);

        if (lane) LaneStrip(p, new RectangleF(parts[1].X, parts[1].Y, parts[1].Width, laneH));
        if (foot) Footer(p, v, new RectangleF(parts[2].X, parts[2].Y, parts[2].Width, footH));
    }

    private Color SpeedFill(HudPainter p) => _speedKph is { } s && _limitKph is { } l
        ? PitLane.State(s, l) switch { SpeedState.Over => p.P.Danger, SpeedState.Near => p.P.Warn, _ => p.P.Good }
        : p.P.LineStrong;

    private void Footer(HudPainter p, HudView v, RectangleF r)
    {
        float d = p.Density;
        string left, right = "";
        Color c;
        if (_inStall)
        {
            left = _stopActive ? "Service under way" : "Waiting for service";
            c = p.P.Accent;
            if (_laneTime is { } t) right = PooledText.Concat("Lane ", Seconds(t));
        }
        else if (_speedKph is { } s && _limitKph is { } l)
        {
            var state = PitLane.State(s, l);
            left = state switch { SpeedState.Over => "Over the limit", SpeedState.Near => "Near the limit", _ => "Under the limit" };
            c = state switch { SpeedState.Over => p.P.Danger, SpeedState.Near => p.P.Warn, _ => p.P.Good };
            double diff = RaceFormat.SpeedValue(Math.Abs(l - s) / 3.6, v.SpeedUnits);
            string unit = RaceFormat.SpeedUnit(v.SpeedUnits);
            right = PooledText.Concat(Whole(diff), PooledText.Concat(" ", unit), s > l ? " over" : " under");
        }
        else
        {
            left = "Pit lane";
            c = p.P.LineStrong;
        }
        p.Fill(r, HudPalette.Alpha(c, 45), p.CornerRadius(4));
        var inner = r.Inflated(-8 * d, 0);
        var fg = c == p.P.Danger ? p.P.DangerText : c == p.P.Warn ? p.P.WarnText : c == p.P.Good ? p.P.GoodText : p.P.Text;
        float rw = right.Length > 0 ? p.Measure(right, p.SmallBold) + 2 : 0;
        if (rw > 0) p.Text(right, p.SmallBold, fg, new RectangleF(inner.Right - rw, inner.Y, rw, inner.Height), HAlign.Right, VAlign.Center, false);
        p.Text(left, p.SmallBold, fg, new RectangleF(inner.X, inner.Y, Math.Max(0, inner.Width - rw - 8 * d), inner.Height), HAlign.Left, VAlign.Center);
    }

    private void LaneStrip(HudPainter p, RectangleF r)
    {
        float d = p.Density;
        float x0 = r.X + 6 * d, x1 = r.Right - 6 * d, cy = r.Y + r.Height * 0.55f;
        float X(float frac) => x0 + Math.Clamp(frac, 0, 1) * (x1 - x0);
        p.Fill(new RectangleF(r.X, cy - 3 * d, r.Width, 6 * d), p.Solid(p.Sunken(0.25)), 3 * d);
        if (_entryKnown) p.Fill(new RectangleF(X(0) - d, cy - 7 * d, 2 * d, 14 * d), p.P.Muted);
        if (_exitKnown) p.Fill(new RectangleF(X(1) - d, cy - 7 * d, 2 * d, 14 * d), p.P.Muted);
        if (_hasBox)
        {
            float bx = X(_boxFrac), bw = 14 * d, bh = 10 * d;
            p.Outline(new RectangleF(bx - bw / 2, cy - 6 * d - bh, bw, bh), p.P.Accent, 2 * d, p.CornerRadius(2));
        }
        for (int i = 0; i < _others; i++)
            if (_otherFrac[i] is >= -0.05f and <= 1.05f) p.Dot(X(_otherFrac[i]), cy + 9 * d, 3.5f * d, _otherColor[i]);
        if (!float.IsNaN(_meFrac))
        {
            float mx = X(_meFrac);
            p.Dot(mx, cy, 7 * d, p.P.Heading);
            p.Dot(mx, cy, 5 * d, p.P.Player);
        }
    }

    private void DrawLane(HudPainter p, HudView v)
    {
        float d = p.Density, gap = 6 * d;
        string right = _limitKph is { } lim ? PooledText.Concat("Limit ", RaceFormat.SpeedKph(lim, v.SpeedUnits)) : "";
        var area = p.Header(_inStall ? "In box" : "Pit lane", right.Length > 0 ? right : null);
        float laneH = 84 * d, scaleH = p.LineHeight(p.Label) + 4 * d + 12 * d + p.LineHeight(p.Small) + 2 * d, tileH = p.BoxedStatHeight(p.Bold) + 6 * d;
        p.NeedBelow(area, laneH + gap + scaleH + gap + tileH);
        float laneBox = Math.Min(laneH, area.Height);
        float y = area.Y + laneBox + gap;
        bool scale = area.Bottom - y >= scaleH + gap + tileH;
        if (scale) y += scaleH + gap;
        bool stats = y + tileH <= area.Bottom + 1;
        Span<RectangleF> parts = stackalloc RectangleF[3];
        p.Arrange(v, "pitbox.lane", area, false, gap, LaneParts, stackalloc float[] { laneBox, scale ? scaleH : -1, stats ? tileH : -1 }, parts);
        DrawLaneDrawing(p, v, new RectangleF(parts[0].X, parts[0].Y, parts[0].Width, laneBox));
        if (scale) DrawSpeedScale(p, v, new RectangleF(parts[1].X, parts[1].Y, parts[1].Width, scaleH));
        if (!stats) return;
        var row = new RectangleF(parts[2].X, parts[2].Y, parts[2].Width, tileH);
        float w = (row.Width - gap * 2) / 3;
        Span<RectangleF> cells = stackalloc RectangleF[3];
        p.Arrange(v, "pitbox.lane.stats", row, true, gap, LaneStats, stackalloc float[] { w, w, w }, cells);
        var c1 = new RectangleF(cells[0].X, row.Y, w, tileH);
        var c2 = new RectangleF(cells[1].X, row.Y, w, tileH);
        var c3 = new RectangleF(cells[2].X, row.Y, w, tileH);
        p.BoxedStat(c1, "To box", DistanceText(_hasBox && !_passedBox && !_inStall ? _toBox : null, v), p.P.Heading);
        p.BoxedStat(c2, "To pit out", DistanceText(_toExit, v), p.P.Heading);
        p.BoxedStat(c3, "Lane time", _laneTime is { } t ? Seconds(t) : RaceFormat.Missing, p.P.Heading);
    }

    private static string DistanceText(double? metres, HudView v) =>
        metres is { } m ? RaceFormat.Distance(Math.Max(0, m), v.Units) : RaceFormat.Missing;

    private void DrawLaneDrawing(HudPainter p, HudView v, RectangleF r)
    {
        float d = p.Density;
        p.Fill(r, p.Solid(p.Sunken(0.2)), p.CornerRadius(4));
        var label = p.Label;
        float lh = p.LineHeight(label);
        float x0 = r.X + 26 * d, x1 = r.Right - 26 * d;
        float cy = r.Y + lh + 24 * d;
        float X(float frac) => x0 + Math.Clamp(frac, -0.04f, 1.04f) * (x1 - x0);
        p.Fill(new RectangleF(r.X + 10 * d, cy - 5 * d, r.Width - 20 * d, 10 * d), p.Solid(p.Raised(0.12)), 5 * d);
        var endColor = p.P.Text;
        float endTop = cy - 12 * d, endBottom = cy + 10 * d;
        DashedEnd(p, X(0), endTop, endBottom, _entryKnown ? endColor : p.P.Faint);
        DashedEnd(p, X(1), endTop, endBottom, _exitKnown ? endColor : p.P.Faint);
        float ly = r.Bottom - lh - 3 * d;
        string pin = p.LabelText("Pit in"), pout = p.LabelText("Pit out");
        float pw = p.Measure(pin, label) + 2, ow = p.Measure(pout, label) + 2;
        p.Text(pin, label, _entryKnown ? p.P.Muted : p.P.Faint, new RectangleF(Math.Max(r.X + 2 * d, X(0) - pw / 2), ly, pw, lh), HAlign.Left, VAlign.Center, false);
        p.Text(pout, label, _exitKnown ? p.P.Muted : p.P.Faint, new RectangleF(Math.Min(r.Right - 2 * d - ow, X(1) - ow / 2), ly, ow, lh), HAlign.Left, VAlign.Center, false);

        if (_hasBox)
        {
            float bx = X(_boxFrac), bs = 16 * d;
            var box = new RectangleF(bx - bs / 2, cy - 7 * d - bs, bs, bs);
            p.Fill(box, HudPalette.Alpha(p.P.Accent, 90), p.CornerRadius(2));
            p.Outline(box, p.P.Accent, 2 * d, p.CornerRadius(2));
            string boxText = p.LabelText("Box");
            float bw = p.Measure(boxText, label) + 2;
            p.Text(boxText, label, p.P.AccentText, new RectangleF(bx - bw / 2, box.Y - lh - 1 * d, bw, lh), HAlign.Left, VAlign.Center, false);
        }

        _labelRight[0] = _labelRight[1] = float.NegativeInfinity;
        for (int i = 0; i < _others; i++)
        {
            float f = _otherFrac[i];
            if (f is < -0.04f or > 1.04f) continue;
            float ox = X(f);
            p.Dot(ox, cy, 5 * d, _otherColor[i]);
            string n = _otherNumber[i];
            if (n.Length == 0) continue;
            float nw = p.Measure(n, p.Small) + 2, nx = ox - nw / 2;
            if (nx > _labelRight[0] + 4 * d && (!_hasBox || Math.Abs(ox - X(_boxFrac)) > nw / 2 + 10 * d))
            {
                p.Text(n, p.Small, _otherColor[i], new RectangleF(nx, cy - 9 * d - p.LineHeight(p.Small), nw, p.LineHeight(p.Small)), HAlign.Left, VAlign.Center, false);
                _labelRight[0] = nx + nw;
            }
        }
        if (!float.IsNaN(_meFrac))
        {
            float mx = X(_meFrac);
            p.Dot(mx, cy, 8 * d, p.P.Heading);
            p.Dot(mx, cy, 6 * d, p.P.Player);
            string speed = _speedKph is { } s ? RaceFormat.Speed(s / 3.6, v.SpeedUnits) : RaceFormat.Missing;
            float sw = p.Measure(speed, p.SmallBold) + 2;
            float sx = Math.Clamp(mx - sw / 2, r.X + 2 * d, r.Right - 2 * d - sw);
            float sy = cy + 10 * d;
            if (sy + p.LineHeight(p.SmallBold) <= ly + 2 * d || Math.Abs(sx + sw / 2 - X(0)) > sw && Math.Abs(sx + sw / 2 - X(1)) > sw)
                p.Text(speed, p.SmallBold, SpeedColor(p), new RectangleF(sx, sy, sw, p.LineHeight(p.SmallBold)), HAlign.Left, VAlign.Center, false);
        }
    }

    private static void DashedEnd(HudPainter p, float x, float top, float bottom, Color c)
    {
        float d = p.Density, dash = 3 * d;
        for (float y = top; y < bottom; y += dash * 2)
            p.Fill(new RectangleF(x - d, y, 2 * d, Math.Min(dash, bottom - y)), c);
    }

    private void DrawSpeedScale(HudPainter p, HudView v, RectangleF r)
    {
        float d = p.Density;
        var label = p.Label;
        float lh = p.LineHeight(label);
        p.Text(p.LabelText("Speed"), label, p.LabelColor, new RectangleF(r.X, r.Y, r.Width, lh), HAlign.Left, VAlign.Center, false);
        if (_limitKph is not { } limit) return;
        double lo = limit * 0.8, hi = limit * 1.1, near = limit - PitLane.NearLimitKph;
        float barY = r.Y + lh + 6 * d, barH = 10 * d, x0 = r.X + 2 * d, x1 = r.Right - 2 * d;
        float X(double kph) => x0 + (float)Math.Clamp((kph - lo) / (hi - lo), 0, 1) * (x1 - x0);
        p.Fill(RectangleF.FromLTRB(x0, barY, X(near), barY + barH), HudPalette.Alpha(p.P.Good, 70));
        p.Fill(RectangleF.FromLTRB(X(near), barY, X(limit), barY + barH), HudPalette.Alpha(p.P.Warn, 80));
        p.Fill(RectangleF.FromLTRB(X(limit), barY, x1, barY + barH), HudPalette.Alpha(p.P.Danger, 80));
        float lx = X(limit);
        p.Fill(new RectangleF(lx - d, barY - 4 * d, 2 * d, barH + 8 * d), p.P.Heading);
        if (_speedKph is { } s)
        {
            float sx = X(s);
            p.Fill(new RectangleF(sx - d, barY, 2 * d, barH), p.P.Heading);
            _needle[0] = new PointF(sx - 5 * d, barY - 7 * d);
            _needle[1] = new PointF(sx + 5 * d, barY - 7 * d);
            _needle[2] = new PointF(sx, barY - d);
            p.G.FillPolygon(p.Brush(p.P.Heading), _needle);
        }
        float ty = barY + barH + 2 * d, th = p.LineHeight(p.Small);
        p.Text(LimitValue(lo, v), p.Small, p.P.Faint, new RectangleF(x0, ty, 60 * d, th), HAlign.Left, VAlign.Center, false);
        p.Text(LimitValue(hi, v), p.Small, p.P.Faint, new RectangleF(x1 - 60 * d, ty, 60 * d, th), HAlign.Right, VAlign.Center, false);
        string limText = PooledText.Concat("Limit ", LimitValue(limit, v));
        float lw = p.Measure(limText, p.Small) + 2;
        p.Text(limText, p.Small, p.P.Heading, new RectangleF(Math.Clamp(lx - lw / 2, x0, x1 - lw), ty, lw, th), HAlign.Left, VAlign.Center, false);
    }

    private static string LimitValue(double kph, HudView v) => Whole(RaceFormat.SpeedValue(kph / 3.6, v.SpeedUnits));

    private void DrawStop(HudPainter p, HudView v)
    {
        float d = p.Density, gap = 6 * d;
        var area = p.Header(_inStall ? "In box" : "Pit lane");
        if (_inStall)
        {
            if (_repairLeft is > 0) Chip(p, "Repairing", p.P.WarnSoft, p.P.WarnText);
            else if (_fill.Rising || _sample && _fuelSeconds is > 0) Chip(p, "Fueling", p.P.WarnSoft, p.P.WarnText);
            else if (_stopActive) Chip(p, "In service", p.P.WarnSoft, p.P.WarnText);
            else Chip(p, "Waiting", p.P.GoodSoft, p.P.GoodText);
        }
        else LimiterChip(p);
        float tileH = p.BoxedStatHeight(p.Big) + 2 * d;
        p.NeedBelow(area, 104 * d + gap + tileH);
        bool tiles = area.Height >= 70 * d + gap + tileH;
        float ring = Math.Max(0, Math.Min(Math.Min(104 * d, area.Width * 0.34f), area.Height - (tiles ? gap + tileH : 0)));
        Span<RectangleF> parts = stackalloc RectangleF[2];
        p.Arrange(v, "pitbox.stop", area, false, gap, StopParts, stackalloc float[] { ring, tiles ? tileH : -1 }, parts);
        var top = new RectangleF(parts[0].X, parts[0].Y, parts[0].Width, ring);
        Span<RectangleF> sides = stackalloc RectangleF[2];
        p.Arrange(v, "pitbox.stop.progress", top, true, gap, StopProgress, stackalloc float[] { ring, 0 }, sides);
        var ringTile = new RectangleF(sides[0].X, top.Y, ring, ring);
        p.Tile(ringTile);
        var dial = ringTile.Inflated(-6 * d, -6 * d);
        var (num, _, label) = Target(v);
        double fraction;
        Color ringColor = p.P.Accent;
        if (_inStall)
        {
            bool repairs = _repairLeft is { } rl && (_fuelSeconds is not { } fs || rl > fs);
            fraction = repairs ? _repairTotal is > 0 and var total ? _repairLeft!.Value / total : 1
                : _fuelProgress is { } prog ? 1 - prog : 0;
            if (repairs) ringColor = p.P.Warn;
        }
        else fraction = _hasBox && !_passedBox && !float.IsNaN(_meFrac) && _boxFrac > 0.01f ? Math.Clamp(_meFrac / _boxFrac, 0, 1) : 0;
        RingDial.Draw(p, dial, fraction, ringColor, num, label, p.P.Heading);

        var rows = new RectangleF(sides[1].X, top.Y, sides[1].Width, ring);
        DrawServices(p, v, rows);

        if (!tiles) return;
        var row = new RectangleF(parts[1].X, parts[1].Y, parts[1].Width, tileH);
        float w = (row.Width - gap * 2) / 3;
        Span<RectangleF> cells = stackalloc RectangleF[3];
        p.Arrange(v, "pitbox.stop.stats", row, true, gap, StopStats, stackalloc float[] { w, w, w }, cells);
        var c1 = new RectangleF(cells[0].X, row.Y, w, tileH);
        var c2 = new RectangleF(cells[1].X, row.Y, w, tileH);
        var c3 = new RectangleF(cells[2].X, row.Y, w, tileH);
        p.BoxedStat(c1, "Rejoin", _exitPos, _exitColor, p.Big);
        string aheadLabel = _aheadNumber.Length > 0 ? PooledText.Concat("Behind ", _aheadNumber) : "Behind";
        p.BoxedStat(c2, aheadLabel, _aheadGap is { } g ? Seconds(g) : RaceFormat.Missing, _aheadGap is null ? p.P.Faint : p.P.Attack, p.Big);
        string traffic = _traffic is not { } n ? RaceFormat.Missing : n == 0 ? "Clear" : RaceFormat.Count(n, "car");
        p.BoxedStat(c3, "Exit traffic", traffic, _traffic is null ? p.P.Faint : _traffic == 0 ? p.P.GoodText : p.P.WarnText, p.Big);
    }

    private void DrawServices(HudPainter p, HudView v, RectangleF r)
    {
        float d = p.Density;
        int shown = Math.Clamp((int)(r.Height / (p.LineHeight(p.Bold) * 0.9f)), 0, 3);
        if (shown == 0 || r.Width < 120 * d) return;
        float rowH = Math.Min(30 * d, r.Height / shown);
        float nameW = Math.Max(p.Measure("Repair", p.Bold), p.Measure("Tires", p.Bold)) + 6 * d, valueW = Math.Max(56 * d, p.Measure("+00.0", p.Bold) + p.Measure(Units.Gal, p.SmallBold) + 4), gap = 8 * d;
        var font = p.Bold;
        float midX = r.X + nameW + gap, midW = Math.Max(0, r.Width - nameW - valueW - gap * 2);
        bool middle = midW >= 40 * d;
        float y = r.Y + (r.Height - rowH * shown) / 2;

        var fuelRow = new RectangleF(r.X, y, r.Width, rowH);
        p.Text("Fuel", font, p.P.Text, new RectangleF(r.X, y, nameW, rowH), HAlign.Left, VAlign.Center);
        if (_fuelRequest is { } req && req > 0)
        {
            if (middle) p.Bar(new RectangleF(midX, y + rowH / 2 - 4 * d, midW, 8 * d), _fuelProgress ?? 0, p.P.Warn);
            double left = _fuelLeft ?? req;
            string text = PooledText.Number("+", RaceFormat.FuelValue(left, v.Units), "0.0", Inv, PooledText.Concat(" ", RaceFormat.FuelUnit(v.Units)));
            p.Number(text, font, p.P.Heading, new RectangleF(r.Right - valueW, y, valueW, rowH), HAlign.Right, VAlign.Center);
        }
        else Quiet(p, "No fuel", midX, midW, valueW, fuelRow);
        y += rowH;
        if (shown < 2) return;

        var tyreRow = new RectangleF(r.X, y, r.Width, rowH);
        p.Text("Tires", font, p.P.Text, new RectangleF(r.X, y, nameW, rowH), HAlign.Left, VAlign.Center);
        int count = 0;
        foreach (var t in TyreOrder) if ((_services & t) != 0) count++;
        if (count > 0)
        {
            float cg = 3 * d, cw = Math.Max(2, (midW - cg * 3) / 4);
            for (int i = 0; i < 4 && middle; i++)
            {
                bool on = (_services & TyreOrder[i]) != 0;
                p.Fill(new RectangleF(midX + i * (cw + cg), y + rowH / 2 - 5 * d, cw, 10 * d), on ? p.P.Good : p.Solid(p.Sunken(0.3)), p.CornerRadius(2));
            }
            p.Text(RaceFormat.Count(count, "tire"), font, p.P.GoodText, new RectangleF(r.Right - valueW, y, valueW, rowH), HAlign.Right, VAlign.Center, false);
        }
        else Quiet(p, "No change", midX, midW, valueW, tyreRow);
        y += rowH;
        if (shown < 3) return;

        var repairRow = new RectangleF(r.X, y, r.Width, rowH);
        p.Text("Repair", font, p.P.Text, new RectangleF(r.X, y, nameW, rowH), HAlign.Left, VAlign.Center);
        if (_repairLeft is { } rep)
        {
            double done = _repairTotal is > 0 and var total ? 1 - rep / total : 0;
            if (middle) p.Bar(new RectangleF(midX, y + rowH / 2 - 4 * d, midW, 8 * d), done, p.P.Warn);
            p.Number(Seconds(rep), font, p.P.WarnText, new RectangleF(r.Right - valueW, y, valueW, rowH), HAlign.Right, VAlign.Center);
        }
        else if (_optLeft is { } opt)
        {
            if (middle) p.Text(PooledText.Concat("Optional ", Seconds(opt)), p.Small, p.P.Muted, new RectangleF(midX, y, midW, rowH), HAlign.Left, VAlign.Center);
            p.Text(RaceFormat.Missing, font, p.P.Faint, new RectangleF(r.Right - valueW, y, valueW, rowH), HAlign.Right, VAlign.Center, false);
        }
        else Quiet(p, (_services & PitServices.FastRepair) != 0 ? "Fast repair set" : "None needed", midX, midW, valueW, repairRow);
    }

    private static void Quiet(HudPainter p, string text, float midX, float midW, float valueW, RectangleF row)
    {
        if (midW >= 40 * p.Density) p.Text(text, p.Small, p.P.Faint, new RectangleF(midX, row.Y, midW, row.Height), HAlign.Left, VAlign.Center);
        p.Text(RaceFormat.Missing, p.Bold, p.P.Faint, new RectangleF(row.Right - valueW, row.Y, valueW, row.Height), HAlign.Right, VAlign.Center, false);
    }
}
