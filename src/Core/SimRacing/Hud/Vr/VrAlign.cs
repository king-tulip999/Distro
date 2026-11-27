using System.Drawing;
using Core.Config;

namespace Core.SimRacing.Hud.Vr;

internal readonly record struct VrSnap(double Value, string? Note);

internal static class VrAlign
{
    public const double MoveReach = 0.03, DistanceReach = 0.03, WidthReach = 0.02, HeightReach = 0.02;
    private const float Far = 10, Same = 0.0005f;

    public static VrSnap Move(HudWidgetConfig me, bool sideways, double value, IEnumerable<HudWidgetConfig> widgets, RaceHudSettings? settings, Func<HudWidgetConfig, double> aspect, Func<HudWidgetConfig, string> name)
    {
        var mine = VrPlacement.Pose(me, settings);
        double shift = sideways ? mine.X - me.VrX : mine.Y - me.VrY;
        double width = mine.Width, height = mine.Width * aspect(me);
        double cx = sideways ? value + shift : mine.X, cy = sideways ? mine.Y : value + shift;
        var r = new RectangleF((float)(cx - width / 2), (float)(-cy - height / 2), (float)width, (float)height);
        var others = Others(me, widgets);
        var boxes = others.Select(o => Apparent(o, mine.Distance, settings, aspect)).ToList();
        var guides = new List<HudGuide>();
        var area = new RectangleF(-Far, -Far, Far * 2, Far * 2);
        var a = HudGeometry.Align(r, boxes, area, 0, (float)MoveReach, guides, alongX: sideways, alongY: !sideways);
        if (guides.Count == 0) return new VrSnap(value, null);
        double result = sideways ? a.X + width / 2 - shift : -(a.Y + height / 2) - shift;
        return new VrSnap(result, Note(sideways, guides, others, boxes, name));
    }

    public static VrSnap Distance(HudWidgetConfig me, double value, IEnumerable<HudWidgetConfig> widgets, Func<HudWidgetConfig, string> name) =>
        Match(value, Others(me, widgets), o => o.VrDistance, DistanceReach, o => $"Same distance as {name(o)}");

    public static VrSnap Width(HudWidgetConfig me, double value, IEnumerable<HudWidgetConfig> widgets, Func<HudWidgetConfig, string> name) =>
        Match(value, Others(me, widgets), o => o.VrWidth, WidthReach, o => $"Same width as {name(o)}");

    public static VrSnap Height(HudWidgetConfig me, double value, IEnumerable<HudWidgetConfig> widgets, Func<HudWidgetConfig, double> aspect, Func<HudWidgetConfig, double> natural, Func<HudWidgetConfig, string> name)
    {
        double content = me.VrWidth * natural(me);
        var best = Match(value, Others(me, widgets), o => o.VrWidth * aspect(o), HeightReach, o => $"Same height as {name(o)}");
        bool fits = Math.Abs(content - value) <= HeightReach;
        if (fits && (best.Note is null || Math.Abs(content - value) <= Math.Abs(best.Value - value))) return new VrSnap(content, "Fits its content");
        return best;
    }

    private static List<HudWidgetConfig> Others(HudWidgetConfig me, IEnumerable<HudWidgetConfig> widgets) =>
        widgets.Where(o => !ReferenceEquals(o, me) && o.Vr == me.Vr && o.Vr != HudVrPlacement.Off && o.VrWidth > 0).ToList();

    private static VrSnap Match(double value, List<HudWidgetConfig> others, Func<HudWidgetConfig, double> of, double reach, Func<HudWidgetConfig, string> note)
    {
        var best = others.Where(o => Math.Abs(of(o) - value) <= reach).OrderBy(o => Math.Abs(of(o) - value)).FirstOrDefault();
        return best is null ? new VrSnap(value, null) : new VrSnap(of(best), note(best));
    }

    public static RectangleF Apparent(HudWidgetConfig w, double atDistance, RaceHudSettings? settings, Func<HudWidgetConfig, double> aspect)
    {
        var p = VrPlacement.Pose(w, settings);
        double k = p.Distance > 0 ? atDistance / p.Distance : 1;
        double width = p.Width * k, height = p.Width * aspect(w) * k;
        return new RectangleF((float)(p.X * k - width / 2), (float)(-p.Y * k - height / 2), (float)width, (float)height);
    }

    private static string Note(bool sideways, List<HudGuide> guides, List<HudWidgetConfig> others, List<RectangleF> boxes, Func<HudWidgetConfig, string> name)
    {
        bool Lengthwise(HudGuide g) => sideways ? Math.Abs(g.From.X - g.To.X) < Same : Math.Abs(g.From.Y - g.To.Y) < Same;
        var mine = guides.Where(g => g.Gap ? !Lengthwise(g) : Lengthwise(g)).ToList();
        if (mine.Any(g => g.Gap)) return "Even spacing";
        var names = new List<string>();
        foreach (var g in mine)
        {
            float at = sideways ? g.From.X : g.From.Y;
            for (int i = 0; i < boxes.Count; i++)
            {
                var b = boxes[i];
                float lo = sideways ? b.Left : b.Top, hi = sideways ? b.Right : b.Bottom;
                if (Math.Abs(lo - at) < Same || Math.Abs(hi - at) < Same || Math.Abs((lo + hi) / 2 - at) < Same)
                {
                    string n = name(others[i]);
                    if (!names.Contains(n)) names.Add(n);
                }
            }
        }
        if (names.Count == 0) return sideways ? "Straight ahead" : "At eye level";
        return "Lined up with " + string.Join(" and ", names.Take(2)) + (names.Count > 2 ? $" + {names.Count - 2}" : "");
    }
}
