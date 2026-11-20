using System.Drawing;
using System.Globalization;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Hud.Widgets;

internal sealed class LedBoardWidget : HudWidget
{
    private static readonly Color Housing = Color.FromArgb(0x0D, 0x0E, 0x13), HousingEdge = Color.FromArgb(0x23, 0x25, 0x2D), Recess = Color.FromArgb(0x06, 0x07, 0x0A),
        Unlit = Color.FromArgb(0x1B, 0x1D, 0x20);

    public const string FromRace = "race", NoChange = "none", Pop = "pop", Wipe = "wipe", Dissolve = "dissolve";
    private const double ChangeSeconds = 0.25, PopGlowSeconds = 0.4, PopFlashSeconds = 0.18, PlaceFlashSeconds = 0.8;
    private const int Levels = 16;
    private const int Cells = LedBoard.Size * LedBoard.Size;

    private readonly LedDot[] _dots = new LedDot[Cells];
    private readonly float[] _levels = new float[Cells];
    private readonly LedDot[] _now = new LedDot[Cells], _before = new LedDot[Cells];
    private string _shown = "", _previous = "", _nowMaskFor = "\0", _beforeMaskFor = "\0";
    private bool _maskFramed;
    private double _changedAt = double.NegativeInfinity;

    public static IReadOnlyList<HudOption> Options { get; } = BuildOptions();

    private static IReadOnlyList<HudOption> BuildOptions()
    {
        var list = new List<HudOption>
        {
            HudOption.Choice("show", "Shows", "gear",
                new HudChoice("gear", "Gear"), new HudChoice("flag", "Flag", "Blank until a flag is out"), new HudChoice("lap", "Lap"), new HudChoice("pos", "Position"), new HudChoice("left", "Laps left")),
            HudOption.Choice("frame", "Frame color", "yellow",
                new HudChoice("yellow", "Yellow"), new HudChoice("white", "White"), new HudChoice("custom", "Custom"),
                new HudChoice("off", "Off", "The number fills the board")),
            HudOption.Color("frameColor", "Frame color", "Until one is picked, the overlay's accent color"),
            HudOption.Choice("change", "When the number changes", Pop,
                new HudChoice(Pop, "Pop", "The new number lands with a glow and a quick flash"),
                new HudChoice(Wipe, "Wipe", "A bright column sweeps the new number in"),
                new HudChoice(Dissolve, "Dissolve", "The dots swap over at random"),
                new HudChoice(NoChange, "No animation")),
            HudOption.Bool("rev", "Rev lights", true, "The frame fills up both sides with RPM and flashes at the shift point. No frame: the gear changes color"),
            HudOption.Bool("limiter", "Limiter flash", true, "On the rev limiter the gear flashes red"),
            HudOption.Bool("placeColors", "Places gained and lost", true, "Green and rolling up when you gain a place, red and rolling down when you lose one"),
            HudOption.Bool("lastLaps", "Last laps", true, "The last 3 laps pulse; the final lap turns the frame white, or the number yellow with no frame"),
            HudOption.Bool("animate", "Animated flags", true, "Off: each flag stays still"),
        };
        var preview = new List<HudChoice> { new(FromRace, "The flag that's out") };
        foreach (var kind in LedFlags.All)
        {
            preview.Add(new HudChoice(kind.Key, kind.Name));
            list.Add(HudOption.Choice(kind.Key, kind.Name, kind.Variants[0].Id, kind.Variants.ToArray()));
        }
        list.Add(HudOption.Choice("previewFlag", "Preview", FromRace, preview.ToArray()) with { Help = "Only this preview; in a race the board shows the flag that's out" });
        return list;
    }

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v)) return;
        var o = v.Options;
        string show = o.Choice("show");
        if (show == "flag")
        {
            var flag = PreviewFlag(p, o) ?? LedBoard.FlagOf(HudFlags.BannerKind(v.Frame.Flags));
            if (LedFlags.Of(flag) is { } kind)
            {
                double t = o.Bool("animate") && !p.ReducedMotion ? v.Now : 0;
                LedFlags.Draw(_dots, _levels, flag, o.Choice(kind.Key), t);
            }
            else Blank();
            Paint(p, HudPalette.FlagYellow);
            return;
        }
        string frame = o.Choice("frame");
        var frameColor = frame switch { "white" => HudPalette.FlagWhite, "custom" => o.ColorOf("frameColor") ?? p.P.Accent, _ => HudPalette.FlagYellow };
        Numbers(p, v, show, frame != "off");
        Paint(p, frameColor);
    }

    private static LedFlag? PreviewFlag(HudPainter p, HudOptions o)
    {
        if (!p.IsPreview) return null;
        string pick = o.Choice("previewFlag");
        foreach (var kind in LedFlags.All)
            if (kind.Key == pick) return kind.Flag;
        return null;
    }

    private void Blank()
    {
        Array.Clear(_dots);
        Array.Clear(_levels);
    }

    private void Numbers(HudPainter p, HudView v, string show, bool framed)
    {
        var o = v.Options;
        var f = v.Frame;
        double now = v.Now;
        var s = f.Session;
        double shift = s.ShiftRpm ?? s.ShiftLightFirstRpm ?? 0;
        double first = s.ShiftLightFirstRpm is { } fl && fl < shift ? fl : s.IdleRpm ?? 0;
        double limit = s.BlinkRpm ?? s.RedlineRpm ?? 0;
        bool sample = p.IsPreview && f.Simulated;
        string text = sample ? SampleText(show, now) : Text(v, show);
        double? rpm = sample ? SampleRpm(now, first, shift, limit) : f.Rpm;
        if (text != _shown)
        {
            _previous = _shown;
            _shown = text;
            _changedAt = _previous.Length == 0 ? double.NegativeInfinity : now;
        }
        if (_maskFramed != framed) { _maskFramed = framed; _nowMaskFor = _beforeMaskFor = "\0"; }
        if (_nowMaskFor != _shown) { LedBoard.Digits(_now, _shown, framed); _nowMaskFor = _shown; }
        if (_beforeMaskFor != _previous) { LedBoard.Digits(_before, _previous, framed); _beforeMaskFor = _previous; }

        bool still = p.ReducedMotion;
        double since = still ? double.PositiveInfinity : now - _changedAt;
        double progress = Math.Clamp(since / ChangeSeconds, 0, 1);
        string change = o.Choice("change");
        bool gear = show == "gear";
        bool rev = gear && o.Bool("rev") && shift > first;
        double fill = rev ? LedBoard.RevFill(rpm, first, shift) : 0;
        bool limiter = gear && o.Bool("limiter") && LedBoard.OnLimiter(rpm, limit);
        bool place = show == "pos" && o.Bool("placeColors") && since < PlaceFlashSeconds && Number(_previous) is { } was && Number(_shown) is { } isNow && was != isNow;
        bool gained = place && Number(_shown) < Number(_previous);
        int? left = show == "left" && o.Bool("lastLaps") ? Number(_shown) : null;
        bool finalLap = left == 1;
        bool flashOn = !still && (long)Math.Floor(now * 12) % 2 == 0;
        bool limiterBlink = limiter && !still && (long)Math.Floor(now * 10) % 2 == 1;
        double lastPulse = left is <= 3 && !still ? 0.55 + 0.45 * LedFlags.Ease(LedFlags.Tri(now / 0.8)) : 1;
        int span = framed ? 22 : 28;
        int roll = place && progress < 1 ? (int)Math.Round(LedFlags.Ease(progress) * span) * (gained ? 1 : -1) : 0;

        LedDot tint = LedDot.White;
        if (!framed)
        {
            if (limiter) tint = LedDot.Red;
            else if (rev && fill >= 1) tint = flashOn ? LedDot.Red : LedDot.Blue;
            else if (rev && fill >= 0.85) tint = LedDot.Red;
            else if (rev && fill >= 0.6) tint = LedDot.Yellow;
            else if (place) tint = gained ? LedDot.Green : LedDot.Red;
            else if (finalLap) tint = LedDot.Yellow;
            else if (change == Pop && since < PopFlashSeconds) tint = LedDot.Yellow;
        }

        for (int y = 0; y < LedBoard.Size; y++)
            for (int x = 0; x < LedBoard.Size; x++)
            {
                int i = y * LedBoard.Size + x;
                if (framed && LedBoard.IsFrame(x, y))
                {
                    var (c, k) = FrameDot(x, y, now, since, limiter, rev, fill, flashOn, place, gained, finalLap, change);
                    _dots[i] = c;
                    _levels[i] = (float)k;
                    continue;
                }
                double level;
                if (roll != 0) level = Lit(_before, x, y + Math.Sign(roll) * Math.Abs(roll)) || Lit(_now, x, y + Math.Sign(roll) * (Math.Abs(roll) - span)) ? 1 : 0;
                else level = DigitLevel(x, y, change, progress, since, framed);
                if (limiterBlink) level = 0;
                else if (level > 0 && left is <= 3) level *= lastPulse;
                _dots[i] = level > 0 ? tint : LedDot.Off;
                _levels[i] = (float)level;
            }
    }

    private static (LedDot, double) FrameDot(int x, int y, double now, double since, bool limiter, bool rev, double fill, bool flashOn,
        bool place, bool gained, bool finalLap, string change)
    {
        if (limiter) return (LedDot.Red, 1);
        if (rev && fill > 0)
        {
            if (fill >= 1) return (flashOn ? LedDot.Red : LedDot.Blue, 1);
            double at = LedBoard.BothSides(x, y);
            if (at > fill) return (LedDot.Off, 0);
            return (at < 0.6 ? LedDot.Green : at < 0.85 ? LedDot.Yellow : LedDot.Red, 1);
        }
        if (place) return (gained ? LedDot.Green : LedDot.Red, (long)Math.Floor(since * 8) % 2 == 0 ? 1 : 0.25);
        if (finalLap) return (LedDot.White, 0.45 + 0.55 * LedFlags.Ease(LedFlags.Tri(now / 0.7)));
        if (change == Pop && since < PopFlashSeconds) return (LedDot.White, 1);
        return (LedDot.Frame, 1);
    }

    private double DigitLevel(int x, int y, string change, double progress, double since, bool framed)
    {
        bool lit = Lit(_now, x, y);
        if (progress >= 1 || change == NoChange)
        {
            if (lit) return 1;
            return change == Pop && since < PopGlowSeconds && Near(x, y) ? 0.55 * (1 - since / PopGlowSeconds) : 0;
        }
        switch (change)
        {
            case Wipe:
                double edgeX = framed ? 3 + progress * 24 : progress * 30;
                if (Math.Abs(x - edgeX) < 0.8) return 1;
                return (x < edgeX ? lit : Lit(_before, x, y)) ? 1 : 0;
            case Dissolve:
                return (LedFlags.Hash(x, y, 3) < progress ? lit : Lit(_before, x, y)) ? 1 : 0;
            default:
                if (lit) return 1;
                return Near(x, y) ? 0.55 * (1 - since / PopGlowSeconds) : 0;
        }
    }

    private bool Near(int x, int y)
    {
        for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
                if (Lit(_now, x + i, y + j)) return true;
        return false;
    }

    private static bool Lit(LedDot[] mask, int x, int y) =>
        (uint)x < LedBoard.Size && (uint)y < LedBoard.Size && mask[y * LedBoard.Size + x] == LedDot.White;

    private static readonly string[] SampleGears = { "1", "2", "3", "4", "5", "6", "5", "4", "3", "2" },
        SamplePlaces = { "5", "4", "4", "3", "4", "4", "2", "3" }, SampleLeft = { "5", "4", "3", "2", "1" },
        SampleLaps = { "8", "9", "10", "11", "12", "13" };

    private const double GearStep = 0.9, PlaceStep = 1.2, LapStep = 1.4, LimiterEvery = 4;

    private static string SampleText(string show, double now) => show switch
    {
        "pos" => SamplePlaces[(int)(now / PlaceStep) % SamplePlaces.Length],
        "left" => SampleLeft[(int)(now / LapStep) % SampleLeft.Length],
        "lap" => SampleLaps[(int)(now / LapStep) % SampleLaps.Length],
        _ => SampleGears[(int)(now / GearStep) % SampleGears.Length],
    };

    private static double? SampleRpm(double now, double first, double shift, double limit)
    {
        if (shift <= first) return null;
        long step = (long)(now / GearStep);
        double phase = now / GearStep - step;
        if (limit > shift && step % LimiterEvery == LimiterEvery - 1 && phase > 0.55) return limit + 50;
        return first + (shift - first) * Math.Min(1.04, phase * 1.12);
    }

    private static int? Number(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    internal static string Text(HudView v, string show)
    {
        var f = v.Frame;
        int me = f.PlayerCarIdx;
        int? n = show switch
        {
            "lap" => f.Lap is > 0 and var lap ? lap : null,
            "pos" => (uint)me < TelemetryFrame.MaxCars && f.CarPosition[me] > 0 ? f.CarPosition[me] : null,
            "left" => v.Race.LapsToGo(f) is { } togo ? (int)Math.Ceiling(togo) : null,
            _ => null,
        };
        if (show == "gear") return f.Gear is { } g ? InputsWidget.GearText(g) : "";
        return n is >= 0 and < 1000 ? n.Value.ToString(CultureInfo.InvariantCulture) : "";
    }

    private void Paint(HudPainter p, Color frame)
    {
        var b = p.Bounds;
        float side = Math.Min(b.Width, b.Height);
        var outer = new RectangleF(b.X + (b.Width - side) / 2, b.Y + (b.Height - side) / 2, side, side);
        float rim = MathF.Max(1, MathF.Round(side * 0.012f));
        p.Fill(outer, HousingEdge, side * 0.09f);
        p.Fill(outer.Inflated(-rim, -rim), Housing, side * 0.09f - rim);
        var recess = outer.Inflated(-side * 0.07f, -side * 0.07f);
        p.Fill(recess, Recess, side * 0.04f);
        float inset = recess.Width * 0.04f;
        var box = recess.Inflated(-inset, -inset);
        float pitch = box.Width / LedBoard.Size, r = pitch * 0.36f;
        for (int y = 0; y < LedBoard.Size; y++)
            for (int x = 0; x < LedBoard.Size; x++)
            {
                int i = y * LedBoard.Size + x;
                var full = _dots[i] switch
                {
                    LedDot.White => HudPalette.FlagWhite,
                    LedDot.Yellow => HudPalette.FlagYellow,
                    LedDot.Green => HudPalette.FlagGreen,
                    LedDot.Blue => HudPalette.FlagBlue,
                    LedDot.Red => HudPalette.FlagRed,
                    LedDot.Orange => HudPalette.FlagOrange,
                    LedDot.Frame => frame,
                    _ => Unlit,
                };
                float level = full == Unlit ? 0 : MathF.Round(_levels[i] * Levels) / Levels;
                float cx = box.X + (x + 0.5f) * pitch, cy = box.Y + (y + 0.5f) * pitch;
                if (level > 0.5f) p.Dot(cx, cy, r * 1.45f, HudPalette.Alpha(full, (int)(70 * (level - 0.5f) * 2)));
                p.Dot(cx, cy, r, level >= 1 ? full : level <= 0 ? Unlit : HudPalette.Mix(Unlit, full, level));
            }
    }
}
