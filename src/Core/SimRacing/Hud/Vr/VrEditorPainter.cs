using System.Drawing;

namespace Core.SimRacing.Hud.Vr;

internal static class VrEditorPainter
{
    public static void Paint(HudPainter p, VrEditor editor, string pickedName)
    {
        var b = p.Bounds;
        p.Fill(b, HudPalette.Alpha(p.P.Bg, 240), p.Look.Corner);
        p.Outline(new RectangleF(b.X + 1, b.Y + 1, b.Width - 2, b.Height - 2), p.P.Accent, 2, p.Look.Corner);
        p.Text("VR widgets", p.Bold, p.P.Heading, new RectangleF(VrEditor.Pad, VrEditor.Pad, 200, 28));
        var rows = editor.Rows();
        foreach (var e in editor.Elements())
        {
            bool hover = editor.Hover.Part == e.Part && editor.Hover.Row == e.Row;
            switch (e.Part)
            {
                case VrEditorPart.Done:
                    Button(p, e.Box, "Done", hover, true);
                    break;
                case VrEditorPart.Previous:
                    Arrow(p, e.Box, hover, -1);
                    break;
                case VrEditorPart.Next:
                    Arrow(p, e.Box, hover, 1);
                    var name = new RectangleF(VrEditor.Pad + VrEditor.ButtonSize + 12, e.Box.Y, VrEditor.Width - 2 * (VrEditor.Pad + VrEditor.ButtonSize + 12), e.Box.Height);
                    p.Text(pickedName, p.Bold, p.P.Text, name, HAlign.Center);
                    break;
                case VrEditorPart.Minus:
                case VrEditorPart.Plus:
                    Button(p, e.Box, "", hover, false);
                    float cx = e.Box.X + e.Box.Width / 2, cy = e.Box.Y + e.Box.Height / 2;
                    p.Line(cx - 6, cy, cx + 6, cy, p.P.Text, 2);
                    if (e.Part == VrEditorPart.Plus) p.Line(cx, cy - 6, cx, cy + 6, p.P.Text, 2);
                    if (e.Part == VrEditorPart.Minus && e.Row < rows.Count)
                        p.Text(rows[e.Row].Label, p.Small, p.P.Muted, new RectangleF(VrEditor.Pad, e.Box.Y - 2, VrEditor.LabelWidth - 6, e.Box.Height + 4));
                    if (e.Part == VrEditorPart.Plus && e.Row < rows.Count)
                        p.Text(VrEditor.Value(rows[e.Row]), p.SmallBold, p.P.Text, new RectangleF(e.Box.Right + 6, e.Box.Y - 2, VrEditor.ValueWidth, e.Box.Height + 4), HAlign.Right);
                    break;
                case VrEditorPart.Track when e.Row < rows.Count:
                    var row = rows[e.Row];
                    float mid = e.Box.Y + e.Box.Height / 2;
                    var track = new RectangleF(e.Box.X, mid - 3, e.Box.Width, 6);
                    p.Fill(track, p.Sunken(0.4), 3);
                    double t = row.Max > row.Min ? (row.Get() - row.Min) / (row.Max - row.Min) : 0;
                    float x = e.Box.X + (float)(Math.Clamp(t, 0, 1) * e.Box.Width);
                    p.Fill(new RectangleF(e.Box.X, mid - 3, x - e.Box.X, 6), p.P.Accent, 3);
                    p.Dot(x, mid, hover ? 9 : 7, p.P.Accent);
                    break;
                case VrEditorPart.Curve when editor.Picked is { } c:
                    Toggle(p, e.Box, "Curve around your head", c.VrCurve, hover);
                    break;
                case VrEditorPart.VrOnly when editor.Picked is { } o:
                    Toggle(p, e.Box, "Hide on the monitor", o.VrOnly, hover);
                    break;
                case VrEditorPart.Face:
                    Button(p, e.Box, "Face my head", hover, false);
                    break;
            }
        }
        p.Text("Right-click or Done gives the mouse back", p.Tiny, p.P.Faint, new RectangleF(VrEditor.Pad, b.Bottom - 22, b.Width - VrEditor.Pad * 2, 16));
        if (editor.CursorShown)
        {
            var c = editor.Cursor;
            p.Dot(c.X, c.Y, 7, p.P.Bg);
            p.Dot(c.X, c.Y, 5, p.P.Heading);
        }
    }

    private static void Button(HudPainter p, RectangleF r, string text, bool hover, bool primary)
    {
        p.Fill(r, primary ? hover ? HudPalette.Mix(p.P.Accent, p.P.Heading, 0.2) : p.P.Accent : hover ? p.Raised(0.18) : p.Raised(0.08), 6);
        if (text.Length > 0) p.Text(text, p.SmallBold, primary ? p.P.OnAccent : p.P.Text, r, HAlign.Center);
    }

    private static void Arrow(HudPainter p, RectangleF r, bool hover, int direction)
    {
        Button(p, r, "", hover, false);
        float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
        p.Line(cx + 4 * direction * -1, cy - 7, cx + 4 * direction, cy, p.P.Text, 2);
        p.Line(cx + 4 * direction, cy, cx + 4 * direction * -1, cy + 7, p.P.Text, 2);
    }

    private static void Toggle(HudPainter p, RectangleF r, string text, bool on, bool hover)
    {
        Button(p, r, "", hover, false);
        var box = new RectangleF(r.X + 8, r.Y + (r.Height - 16) / 2, 16, 16);
        if (on) p.Fill(box, p.P.Accent, 3);
        else p.Outline(box, p.P.LineStrong, 1.5f, 3);
        if (on)
        {
            p.Line(box.X + 3.5f, box.Y + 8, box.X + 7, box.Y + 11.5f, p.P.OnAccent, 2);
            p.Line(box.X + 7, box.Y + 11.5f, box.X + 12.5f, box.Y + 4.5f, p.P.OnAccent, 2);
        }
        p.Text(text, p.Small, p.P.Text, new RectangleF(box.Right + 8, r.Y, r.Right - box.Right - 12, r.Height));
    }
}
