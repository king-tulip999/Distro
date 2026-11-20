using System.Drawing;

namespace Core.SimRacing.Hud.Widgets;

internal sealed partial class InputsWidget
{
    private float _ox, _oy;

    private void Fit(HudPainter p, float w, float h)
    {
        var b = p.Bounds;
        _k = Math.Max(0.1f, Math.Min(b.Height / h, b.Width / w));
        _ox = (b.Width - S(w)) / 2;
        _oy = (b.Height - S(h)) / 2;
    }

    private RectangleF At(float x, float y, float w, float h) => new(_ox + S(x), _oy + S(y), S(w), S(h));

    private static float Round(HudPainter p, float max) => Math.Max(0, Math.Min(p.Look.Corner, max));

    internal static SizeF BareSize(Shows on) => new(Math.Max(60, (on.GearDial ? DialRow + (on.TraceGraph ? 8 : 0) : 0) + (on.TraceGraph ? DialTraceW : 0)), DialRow);
}
