using System.Drawing;
using System.Globalization;
using Core.SimRacing.Race;

namespace Core.SimRacing.Hud.Widgets;

internal sealed partial class WeatherWidget
{
    public const string PanelLook = "panel", StripLook = "strip";
    public const float PanelW = 230, PanelH = 370;

    private const float DialShare = 0.62f;
    private readonly List<(string Label, string Value, Color Color)> _rows = new();

    private void DrawPanel(HudPainter p, HudView v, Func<double?, string> temp)
    {
        var f = v.Frame;
        float d = p.Density;
        var area = p.Header("Weather", f.Session.Skies.Length > 0 ? SentenceCase(f.Session.Skies) : null, p.P.Heading);
        var rows = v.Options.Columns("rows");
        bool wind = v.Options.Bool("wind");
        bool track = v.Options.Bool("wetBar");
        float rowH = p.LineHeight(p.SmallBold) + 9 * d;
        float wetH = track ? p.LineHeight(p.SmallBold) + 22 * d : 0;
        float listH = rowH * rows.Count;
        float dialRoom = area.Height - listH - wetH;
        double heading = v.Options.Bool("turn") && f.YawNorth is { } yaw ? yaw : 0;
        if (wind && dialRoom >= 70 * d)
        {
            float side = Math.Min(area.Width * DialShare, dialRoom - 8 * d);
            var top = area.Top(side + 8 * d, out area);
            var box = new RectangleF(top.X + (top.Width - side) / 2, top.Y + 4 * d, side, side);
            double? toward = f.WindDir is { } from ? from + Math.PI : null;
            WindArrow.Draw(p, box, f.WindSpeed, toward, heading, "headtail", v.Units, true);
        }
        _rows.Clear();
        foreach (var id in rows)
        {
            switch (id)
            {
                case "track": _rows.Add(("Track temp", temp(f.TrackTemp), p.P.Heading)); break;
                case "air": _rows.Add(("Air temp", temp(f.AirTemp), p.P.Heading)); break;
                case "wind":
                    string dir = f.WindDir is { } from ? RaceFormat.Compass(from + Math.PI) : RaceFormat.Missing;
                    _rows.Add(("Wind", f.WindSpeed is { } ws ? PooledText.Number("", RaceFormat.SpeedValue(ws, v.Units), "0", CultureInfo.InvariantCulture, " " + RaceFormat.SpeedUnit(v.Units) + " " + dir) : RaceFormat.Missing, p.P.Heading));
                    break;
                case "humidity": _rows.Add(("Humidity", f.Humidity is { } h ? PooledText.Number("", h * 100, "0", CultureInfo.InvariantCulture, "%") : RaceFormat.Missing, p.P.Heading)); break;
                case "rain": _rows.Add(("Rain", f.Precipitation is { } r ? PooledText.Number("", r * 100, "0", CultureInfo.InvariantCulture, "%") : RaceFormat.Missing, f.Precipitation is > 0 ? p.P.WarnText : p.P.Heading)); break;
                case "fog": _rows.Add(("Fog", f.Fog is { } fog ? PooledText.Number("", fog * 100, "0", CultureInfo.InvariantCulture, "%") : RaceFormat.Missing, p.P.Heading)); break;
                case "rubber": _rows.Add(("Rubber", Rubber(f.CurrentSession?.Rubber), p.P.Heading)); break;
                case "trend":
                    _rows.Add(("Track 10 min", v.Race.Weather.TrackPerWindow is { } tr ? (tr < 0 ? "−" : "+") + RaceFormat.TempDelta(Math.Abs(tr), v.Units) : RaceFormat.Missing, p.P.Heading));
                    break;
            }
        }
        var list = area.Top(Math.Min(area.Height, listH), out area);
        float y = list.Y;
        for (int i = 0; i < _rows.Count; i++)
        {
            if (y + rowH > list.Bottom + 1) { p.Overflow?.Add(("rows", rows[i])); continue; }
            var row = new RectangleF(list.X, y, list.Width, rowH);
            p.Line(row.X, MathF.Round(row.Y) + 0.5f, row.Right, MathF.Round(row.Y) + 0.5f, HudPalette.Alpha(p.P.LineStrong, 70), 1);
            var (label, value, color) = _rows[i];
            float vw = p.Measure(value, p.SmallBold) + 4;
            p.Text(p.LabelText(label), p.Label, p.LabelColor, new RectangleF(row.X + 2 * d, row.Y, Math.Max(0, row.Width - vw - 8 * d), row.Height), HAlign.Left, VAlign.Center);
            p.Text(value, p.SmallBold, color, new RectangleF(row.Right - vw - 2 * d, row.Y, vw, row.Height), HAlign.Right, VAlign.Center, false);
            p.Item("rows", rows[i], row);
            y += rowH;
        }
        if (track && area.Height >= wetH * 0.8f) Wetness(p, v, new RectangleF(area.X, area.Bottom - Math.Min(area.Height, wetH), area.Width, Math.Min(area.Height, wetH)));
    }

    private static string Rubber(string? rubber) => string.IsNullOrEmpty(rubber) ? RaceFormat.Missing
        : char.ToUpperInvariant(rubber[0]) + (rubber.EndsWith(" usage", StringComparison.OrdinalIgnoreCase) ? rubber[1..^6] : rubber[1..]);

    private void Wetness(HudPainter p, HudView v, RectangleF r)
    {
        var f = v.Frame;
        float d = p.Density;
        var barBox = new RectangleF(r.X, r.Y + 4 * d, r.Width, 10 * d);
        p.Region("wetBar", r);
        p.Fill(barBox, p.P.TraceBack, p.CornerRadius(5));
        int level = f.TrackWetness is { } w ? Math.Clamp(w - 1, 0, 6) : 0;
        if (level > 0)
            p.Fill(new RectangleF(barBox.X, barBox.Y, barBox.Width * level / 6f, barBox.Height), p.P.ClutchInput, p.CornerRadius(5));
        var text = new RectangleF(r.X + 2 * d, barBox.Bottom + 4 * d, r.Width - 4 * d, r.Bottom - barBox.Bottom - 4 * d);
        p.Text("TRACK", p.Label, p.LabelColor, text, HAlign.Left, VAlign.Center);
        bool wet = f.TrackWetness is > 1 || f.DeclaredWet == true;
        string state = (f.DeclaredWet == true ? "DECLARED WET · " : "") + RaceFormat.Wetness(f.TrackWetness).ToUpperInvariant();
        p.Text(state, p.SmallBold, wet ? p.P.WarnText : p.P.Heading, text, HAlign.Right, VAlign.Center, false);
    }
}
