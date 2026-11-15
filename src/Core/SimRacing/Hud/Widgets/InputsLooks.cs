using System.Drawing;
using System.Globalization;
using Core.SimRacing.Race;

namespace Core.SimRacing.Hud.Widgets;

internal sealed partial class InputsWidget
{
    private float _ox, _oy;
    private string? _unitOf, _unitSuffix;

    private void Fit(HudPainter p, float w, float h)
    {
        var b = p.Bounds;
        _k = Math.Max(0.1f, Math.Min(b.Height / h, b.Width / w));
        _ox = (b.Width - S(w)) / 2;
        _oy = (b.Height - S(h)) / 2;
    }

    private RectangleF At(float x, float y, float w, float h) => new(_ox + S(x), _oy + S(y), S(w), S(h));

    private static float Round(HudPainter p, float max) => Math.Max(0, Math.Min(p.Look.Corner, max));

    private string SpeedWithUnit(HudPainter p, HudView v)
    {
        string unit = RaceFormat.SpeedUnit(v.Units);
        if (!ReferenceEquals(unit, _unitOf)) { _unitOf = unit; _unitSuffix = " " + p.LabelText(unit); }
        return v.Frame.Speed is { } ms ? PooledText.Number("", RaceFormat.SpeedValue(ms, v.Units), "0", CultureInfo.InvariantCulture, _unitSuffix!) : RaceFormat.Missing + _unitSuffix;
    }

    private void BareTrace(HudPainter p, RectangleF r, double now, Color halo)
    {
        float lineW = S(1.6f), steerW = S(1);
        p.G.SetClip(r);
        if (_steering) Lines(p, r, now, TraceSeconds, 3, halo, null, steerW + S(4));
        if (_brake) Lines(p, r, now, TraceSeconds, 1, halo, null, lineW + S(4));
        if (_throttle) Lines(p, r, now, TraceSeconds, 0, halo, null, lineW + S(4));
        if (_throttle) Lines(p, r, now, TraceSeconds, 0, p.P.ThrottleInput, null, lineW);
        if (_brake) Lines(p, r, now, TraceSeconds, 1, p.P.BrakeInput, _abs ? p.P.AbsInput : null, lineW);
        if (_steering) Lines(p, r, now, TraceSeconds, 3, p.P.Muted, null, steerW);
        p.G.ResetClip();
    }

    private void HaloText(HudPainter p, string text, Font font, Color color, RectangleF box, Color halo)
    {
        float d = S(1);
        p.Text(text, font, halo, new RectangleF(box.X - d, box.Y - d, box.Width, box.Height), HAlign.Center, VAlign.Center, false);
        p.Text(text, font, halo, new RectangleF(box.X + d, box.Y - d, box.Width, box.Height), HAlign.Center, VAlign.Center, false);
        p.Text(text, font, halo, new RectangleF(box.X - d, box.Y + d, box.Width, box.Height), HAlign.Center, VAlign.Center, false);
        p.Text(text, font, halo, new RectangleF(box.X + d, box.Y + d, box.Width, box.Height), HAlign.Center, VAlign.Center, false);
        p.Text(text, font, color, box, HAlign.Center, VAlign.Center, false);
    }

    private void HaloDisplay(HudPainter p, string text, float px, Color color, RectangleF box, Color halo)
    {
        float d = S(1.5f);
        p.Display(text, px, halo, new RectangleF(box.X - d, box.Y, box.Width, box.Height), HAlign.Center, VAlign.Center);
        p.Display(text, px, halo, new RectangleF(box.X + d, box.Y, box.Width, box.Height), HAlign.Center, VAlign.Center);
        p.Display(text, px, halo, new RectangleF(box.X, box.Y - d, box.Width, box.Height), HAlign.Center, VAlign.Center);
        p.Display(text, px, halo, new RectangleF(box.X, box.Y + d, box.Width, box.Height), HAlign.Center, VAlign.Center);
        p.Display(text, px, color, box, HAlign.Center, VAlign.Center);
    }

    private void DrawBare(HudPainter p, HudView v, in Live live)
    {
        var size = BareSize(_on);
        Fit(p, size.Width, size.Height);
        var halo = Color.FromArgb(150, 0, 0, 0);
        var floor = At(4, 80, 296, 0);
        p.Line(floor.Left, floor.Top, floor.Right, floor.Top, halo, S(3));
        p.Line(floor.Left, floor.Top, floor.Right, floor.Top, Color.FromArgb(46, 255, 255, 255), 1);
        if (_on.TraceGraph) BareTrace(p, At(4, 6, 296, 74), live.Now, Color.FromArgb(110, 0, 0, 0));
        if (_gear) HaloDisplay(p, GearText(v.Frame.Gear), S(48), GearColor(p, live), At(300, 16, 120, 36), halo);
        if (_speed) HaloText(p, SpeedWithUnit(p, v), p.Font(S(13), true), p.P.Heading, _gear ? At(300, 63, 120, 17) : At(300, 36, 120, 17), halo);
    }

    internal static SizeF BareSize(Shows on) => new(on.Gear || on.Speed ? 420 : 304, 90);
}
