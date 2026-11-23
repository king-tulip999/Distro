using System.Drawing;
using Core.Config;
using Units = Core.Services.CapsUnits;

namespace Core.SimRacing.Hud.Vr;

internal enum VrEditorPart { None, Done, Previous, Next, Minus, Plus, Track, Curve, VrOnly, Face, Grip }

internal readonly record struct VrEditorHit(VrEditorPart Part, int Row = -1)
{
    public static readonly VrEditorHit Nothing = new(VrEditorPart.None);
}

internal readonly record struct VrEditorElement(VrEditorPart Part, int Row, RectangleF Box);

internal sealed record VrEditorRow(string Label, double Min, double Max, double Step, string Unit, Func<double> Get, Action<double> Set);

internal enum VrEditorResult { None, Redraw, Changed, Close }

internal sealed class VrEditor
{
    public const float Width = 480, Pad = 14, RowHeight = 34, RowGap = 4, ButtonSize = 30, LabelWidth = 84, ValueWidth = 70;
    public const double MouseSpeed = 0.6, PanelMetresPerCount = 0.0006, PanelWidthStep = 0.02;

    private readonly RaceHudSettings _settings;
    private List<HudWidgetConfig?> _candidates = new();
    private int _dragRow = -1;
    private float _dragFromX;
    private double _dragFromValue;
    private bool _panelDrag;

    public bool MovingPanel => _panelDrag;

    public VrEditor(RaceHudSettings settings) => _settings = settings;

    public int Pick { get; private set; }

    public PointF Cursor { get; private set; } = new(Width / 2, 120);

    public bool CursorShown { get; private set; }

    public VrEditorHit Hover { get; private set; } = VrEditorHit.Nothing;

    public IReadOnlyList<HudWidgetConfig?> Candidates => _candidates;

    public HudWidgetConfig? Picked => Pick >= 0 && Pick < _candidates.Count ? _candidates[Pick] : null;

    public bool AllPicked => Pick >= 0 && Pick < _candidates.Count && _candidates[Pick] is null;

    public string PickedName(Func<HudWidgetConfig, string> name) => Picked is { } w ? name(w) : "All cockpit widgets";

    public void SetCandidates(List<HudWidgetConfig?> candidates)
    {
        var keep = Picked;
        bool all = AllPicked;
        _candidates = candidates;
        int at = all ? candidates.IndexOf(null) : keep is null ? -1 : candidates.FindIndex(w => ReferenceEquals(w, keep));
        Pick = at >= 0 ? at : 0;
    }

    public void Step(int delta)
    {
        if (_candidates.Count == 0) return;
        Pick = ((Pick + delta) % _candidates.Count + _candidates.Count) % _candidates.Count;
        _dragRow = -1;
    }

    public List<VrEditorRow> Rows()
    {
        var rows = new List<VrEditorRow>();
        double reach = HudWidgetConfig.VrReach * 100;
        if (Picked is { } w)
        {
            rows.Add(new("Sideways", -reach, reach, 1, Units.Cm, () => w.VrX * 100, v => w.VrX = v / 100));
            rows.Add(new("Height", -reach, reach, 1, Units.Cm, () => w.VrY * 100, v => w.VrY = v / 100));
            rows.Add(new("Distance", HudWidgetConfig.MinVrDistance * 100, HudWidgetConfig.MaxVrDistance * 100, 1, Units.Cm, () => w.VrDistance * 100, v => w.VrDistance = v / 100));
            rows.Add(new("Width", HudWidgetConfig.MinVrWidth * 100, HudWidgetConfig.MaxVrWidth * 100, 1, Units.Cm, () => w.VrWidth * 100, v => w.VrWidth = v / 100));
            rows.Add(new("Turn", -HudWidgetConfig.MaxVrAngle, HudWidgetConfig.MaxVrAngle, VrAdjust.AngleStep, "°", () => w.VrTurn, v => w.VrTurn = v));
            rows.Add(new("Tilt", -HudWidgetConfig.MaxVrAngle, HudWidgetConfig.MaxVrAngle, VrAdjust.AngleStep, "°", () => w.VrTilt, v => w.VrTilt = v));
        }
        else if (AllPicked)
        {
            double far = RaceHudSettings.MaxVrShiftDistance * 100;
            rows.Add(new("Sideways", -reach, reach, 1, Units.Cm, () => _settings.VrShiftX * 100, v => _settings.VrShiftX = v / 100));
            rows.Add(new("Height", -reach, reach, 1, Units.Cm, () => _settings.VrShiftY * 100, v => _settings.VrShiftY = v / 100));
            rows.Add(new("Distance", -far, far, 1, Units.Cm, () => _settings.VrShiftDistance * 100, v => _settings.VrShiftDistance = v / 100));
        }
        return rows;
    }

    public const float FooterHeight = 34;

    public float Height => Layout(Rows().Count).Max(e => e.Box.Bottom) + Pad + FooterHeight;

    public List<VrEditorElement> Elements() => Layout(Rows().Count);

    private List<VrEditorElement> Layout(int rows)
    {
        var list = new List<VrEditorElement>
        {
            new(VrEditorPart.Done, -1, new RectangleF(Width - Pad - 76, Pad, 76, 28)),
            new(VrEditorPart.Grip, -1, new RectangleF(Pad, Pad, Width - Pad * 2 - 76 - 8, 28)),
            new(VrEditorPart.Previous, -1, new RectangleF(Pad, 52, ButtonSize + 6, 32)),
            new(VrEditorPart.Next, -1, new RectangleF(Width - Pad - ButtonSize - 6, 52, ButtonSize + 6, 32)),
        };
        float y = 96;
        for (int i = 0; i < rows; i++)
        {
            float x = Pad + LabelWidth;
            float trackRight = Width - Pad - ValueWidth - ButtonSize - 6;
            list.Add(new(VrEditorPart.Minus, i, new RectangleF(x, y + 2, ButtonSize, ButtonSize)));
            list.Add(new(VrEditorPart.Track, i, new RectangleF(x + ButtonSize + 6, y, trackRight - x - ButtonSize - 12, RowHeight)));
            list.Add(new(VrEditorPart.Plus, i, new RectangleF(trackRight, y + 2, ButtonSize, ButtonSize)));
            y += RowHeight + RowGap;
        }
        if (Picked is not null)
        {
            float half = (Width - Pad * 3) / 2;
            y += 6;
            if (AppIdentity.SimHub) list.Add(new(VrEditorPart.Curve, -1, new RectangleF(Pad, y, Width - Pad * 2, 30)));
            else
            {
                list.Add(new(VrEditorPart.Curve, -1, new RectangleF(Pad, y, half, 30)));
                list.Add(new(VrEditorPart.VrOnly, -1, new RectangleF(Pad * 2 + half, y, half, 30)));
            }
            y += 36;
            list.Add(new(VrEditorPart.Face, -1, new RectangleF(Pad, y, Width - Pad * 2, 30)));
        }
        return list;
    }

    public VrEditorHit HitTest(PointF p)
    {
        foreach (var e in Elements())
            if (e.Box.Contains(p)) return new VrEditorHit(e.Part, e.Row);
        return VrEditorHit.Nothing;
    }

    public VrEditorResult MoveBy(double dx, double dy)
    {
        if (_panelDrag)
        {
            _settings.VrPanelX += dx * PanelMetresPerCount;
            _settings.VrPanelY -= dy * PanelMetresPerCount;
            _settings.ClampVrPanel();
            return VrEditorResult.Redraw;
        }
        return MoveTo(new PointF((float)Math.Clamp(Cursor.X + dx * MouseSpeed, 0, Width), (float)Math.Clamp(Cursor.Y + dy * MouseSpeed, 0, Height)), true);
    }

    public VrEditorResult MoveTo(PointF p, bool cursor)
    {
        Cursor = p;
        CursorShown = cursor;
        var hover = HitTest(p);
        bool redraw = hover != Hover || cursor;
        Hover = hover;
        if (_dragRow >= 0 && DragTo(_dragRow, p.X)) return VrEditorResult.Changed;
        return redraw ? VrEditorResult.Redraw : VrEditorResult.None;
    }

    public VrEditorResult Press(PointF? at = null)
    {
        if (at is { } p) MoveTo(p, CursorShown);
        var hit = HitTest(Cursor);
        var rows = Rows();
        switch (hit.Part)
        {
            case VrEditorPart.Done: return VrEditorResult.Close;
            case VrEditorPart.Grip when at is null:
                _panelDrag = true;
                return VrEditorResult.Redraw;
            case VrEditorPart.Previous: Step(-1); return VrEditorResult.Redraw;
            case VrEditorPart.Next: Step(1); return VrEditorResult.Redraw;
            case VrEditorPart.Minus when hit.Row < rows.Count: return Nudge(rows[hit.Row], -1);
            case VrEditorPart.Plus when hit.Row < rows.Count: return Nudge(rows[hit.Row], 1);
            case VrEditorPart.Track when hit.Row < rows.Count:
                _dragRow = hit.Row;
                _dragFromX = Cursor.X;
                _dragFromValue = rows[hit.Row].Get();
                return VrEditorResult.Redraw;
            case VrEditorPart.Curve when Picked is { } c: c.VrCurve = !c.VrCurve; return VrEditorResult.Changed;
            case VrEditorPart.VrOnly when Picked is { } o: o.VrOnly = !o.VrOnly; return VrEditorResult.Changed;
            case VrEditorPart.Face when Picked is { } f:
                (f.VrTurn, f.VrTilt) = VrPlacement.FacingHead(f.VrX, f.VrY, f.VrDistance);
                return VrEditorResult.Changed;
            default: return VrEditorResult.None;
        }
    }

    public VrEditorResult Release()
    {
        _dragRow = -1;
        if (!_panelDrag) return VrEditorResult.None;
        _panelDrag = false;
        return VrEditorResult.Redraw;
    }

    public VrEditorResult Wheel(int notches)
    {
        var hit = HitTest(Cursor);
        if (hit.Part == VrEditorPart.Grip && notches != 0)
        {
            double before = _settings.VrPanelWidth;
            _settings.VrPanelWidth += Math.Sign(notches) * PanelWidthStep;
            _settings.ClampVrPanel();
            return Math.Abs(_settings.VrPanelWidth - before) > 1e-9 ? VrEditorResult.Redraw : VrEditorResult.None;
        }
        var rows = Rows();
        if (hit.Row < 0 || hit.Row >= rows.Count || notches == 0) return VrEditorResult.None;
        return Nudge(rows[hit.Row], Math.Sign(notches));
    }

    private VrEditorResult Nudge(VrEditorRow row, int direction) => SetValue(row, row.Get() + direction * row.Step) ? VrEditorResult.Changed : VrEditorResult.None;

    private bool DragTo(int index, float x)
    {
        var rows = Rows();
        if (index < 0 || index >= rows.Count) return false;
        var track = Elements().First(e => e.Part == VrEditorPart.Track && e.Row == index).Box;
        var row = rows[index];
        return SetValue(row, _dragFromValue + (x - _dragFromX) / track.Width * (row.Max - row.Min));
    }

    private bool SetValue(VrEditorRow row, double value)
    {
        double before = row.Get();
        double v = Math.Clamp(Math.Round(value / row.Step) * row.Step, row.Min, row.Max);
        if (Math.Abs(v - before) < 1e-9) return false;
        row.Set(v);
        Picked?.Clamp();
        if (AllPicked) _settings.ClampVrShift();
        return true;
    }

    public static string Value(VrEditorRow row) => row.Unit == "°" ? $"{row.Get():0}°" : $"{row.Get():0} {row.Unit}";
}
