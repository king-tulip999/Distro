using System.Drawing;

namespace Core.SimRacing.Hud;

internal sealed class HudSimBar
{
    private const string Label = "Simulation";
    private const float Height = 26, Pad = 12, Dot = 8, Gap = 8;

    private readonly HudWindow _window = new("Velocita · Simulation");
    private readonly HudPainter _painter = new();
    private RectangleF _drawnAt;
    private HudPalette? _drawnPalette;
    private bool _closed;

    public bool Visible => _window.Visible;

    public void Show(HudHost host, HudPalette palette, float top)
    {
        if (_closed) return;
        var monitor = HudGeometry.MonitorFor("primary", host.Monitors);
        float scale = monitor.Scale;
        float width = Pad + Dot + Gap + _painter.Measure(Label, _painter.SmallBold) + Pad;
        var rect = new RectangleF(monitor.Bounds.Left + (monitor.Bounds.Width - width * scale) / 2, monitor.Bounds.Top + top * scale, width * scale, Height * scale);
        if (_window.Visible && rect == _drawnAt && ReferenceEquals(palette, _drawnPalette)) { _window.KeepOnTop(); return; }
        _drawnAt = rect;
        _drawnPalette = palette;
        _window.Rect = rect;
        var size = new Size((int)Math.Ceiling(rect.Width), (int)Math.Ceiling(rect.Height));
        var g = _window.Surface(size);
        var look = host.Settings.Appearance;
        var p = _painter;
        p.Begin(g, scale, new SizeF(width, Height), palette, look);
        p.Fill(p.Bounds, HudPalette.Alpha(p.P.Surface, 235), Height / 2);
        p.Outline(new RectangleF(0.5f, 0.5f, width - 1, Height - 1), p.P.Warn, 1.2f, Height / 2);
        p.Fill(new RectangleF(Pad, (Height - Dot) / 2, Dot, Dot), p.P.Warn, Dot / 2);
        p.Text(Label, p.SmallBold, p.P.Text, new RectangleF(Pad + Dot + Gap, 0, width - Pad * 2 - Dot - Gap, Height), HAlign.Left, VAlign.Center, false);
        p.End(_window.SurfaceDc, size);
        if (!_window.Visible) _window.Show();
        _window.Present(new Point((int)rect.X, (int)rect.Y));
        _window.KeepOnTop();
    }

    public void Hide()
    {
        if (!_window.Visible) return;
        _window.Hide();
        _window.ReleaseSurface();
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        _window.Hide();
        _window.Dispose();
        _painter.Dispose();
    }
}
