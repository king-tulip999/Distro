using System.Drawing;

namespace Core.SimRacing.Hud.Vr;

internal static class VrFlash
{
    public const float Width = 360, Pad = 12, TitleHeight = 34, RowHeight = 30, FooterHeight = 26;
    public const int MaxRows = 7;
    public const double ShowSeconds = 3, ToastSeconds = 2.5;
    public const float ToastWidth = 320, ToastTitle = 30, ToastDetail = 22;

    public static SizeF ToastSize(bool detail) => new(ToastWidth, Pad * 2 + ToastTitle + (detail ? ToastDetail : 0));

    public static void PaintToast(HudPainter p, string title, string? detail)
    {
        var b = p.Bounds;
        p.Fill(b, HudPalette.Alpha(p.P.Bg, 240), p.Look.Corner);
        p.Outline(new RectangleF(b.X + 1, b.Y + 1, b.Width - 2, b.Height - 2), p.P.Accent, 2, p.Look.Corner);
        p.Text(title, p.Bold, p.P.Heading, new RectangleF(Pad, Pad, ToastWidth - Pad * 2, ToastTitle), HAlign.Center);
        if (detail is not null) p.Text(detail, p.Small, p.P.Muted, new RectangleF(Pad, Pad + ToastTitle, ToastWidth - Pad * 2, ToastDetail), HAlign.Center);
    }

    public static (int First, int Count) Window(int count, int current)
    {
        int shown = Math.Min(count, MaxRows);
        int first = Math.Clamp(current - shown / 2, 0, Math.Max(0, count - shown));
        return (first, shown);
    }

    public static SizeF Size(int count) => new(Width, Pad + TitleHeight + Window(count, 0).Count * RowHeight + FooterHeight + Pad);

    public static void Paint(HudPainter p, IReadOnlyList<string> names, int current, string footer)
    {
        var b = p.Bounds;
        p.Fill(b, HudPalette.Alpha(p.P.Bg, 240), p.Look.Corner);
        p.Outline(new RectangleF(b.X + 1, b.Y + 1, b.Width - 2, b.Height - 2), p.P.Accent, 2, p.Look.Corner);
        p.Text("Layout", p.Bold, p.P.Heading, new RectangleF(Pad + 4, Pad, Width - Pad * 2, TitleHeight - 6));
        var (first, count) = Window(names.Count, current);
        float y = Pad + TitleHeight;
        for (int i = first; i < first + count; i++)
        {
            var row = new RectangleF(Pad, y, Width - Pad * 2, RowHeight - 2);
            bool on = i == current;
            if (on)
            {
                p.Fill(row, HudPalette.Alpha(p.P.Accent, 60), 6);
                p.Fill(new RectangleF(row.X, row.Y, 3, row.Height), p.P.Accent, 1.5f);
            }
            p.Text(names[i], on ? p.Bold : p.Small, on ? p.P.Heading : p.P.Muted, new RectangleF(row.X + 12, row.Y, row.Width - 20, row.Height));
            y += RowHeight;
        }
        string more = names.Count > count ? $"{current + 1} of {names.Count} · " : "";
        p.Text(more + footer, p.Tiny, p.P.Faint, new RectangleF(Pad + 4, y + 4, Width - Pad * 2 - 8, FooterHeight - 6));
    }
}
