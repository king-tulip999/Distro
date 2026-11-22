using System.Drawing;
using System.Drawing.Drawing2D;
using Core.Overlay;

namespace Core.SimRacing.Hud;

internal static class DotLabels
{
    public const float ReferencePx = 10;
    public const float SmallestPx = 6.5f;
    public const float Inner = 0.88f;
    public const float TallestShare = 1.1f;

    private static (float Top, float Bottom)? _digits;

    public static (float Top, float Bottom) DigitBand
    {
        get
        {
            if (_digits is { } known) return known;
            float top = 0.3f, bottom = 1.0f;
            try
            {
                using var path = new GraphicsPath();
                const float em = 100;
                path.AddString("0123456789", OverlayFonts.Semibold, (int)FontStyle.Regular, em, PointF.Empty, StringFormat.GenericTypographic);
                var b = path.GetBounds();
                if (b.Height > 0) { top = b.Top / em; bottom = b.Bottom / em; }
            }
            catch (ArgumentException)
            {
            }
            _digits = (top, bottom);
            return (top, bottom);
        }
    }

    public static float FitPx(float widthAtReference, float digitHeightShare, float radius, float textScale)
    {
        if (widthAtReference <= 0 || radius <= 0 || textScale <= 0) return 0;
        float room = radius * Inner;
        float w = widthAtReference, h = digitHeightShare * ReferencePx * textScale;
        float byCorner = 2 * room / MathF.Sqrt(w * w + h * h);
        float byHeight = TallestShare * room / h;
        float px = ReferencePx * Math.Min(byCorner, byHeight);
        px = MathF.Floor(px * 2) / 2;
        return px >= SmallestPx ? px : 0;
    }

    public static bool Fits(float width, float digitHeight, float radius)
    {
        float room = radius * Inner;
        return width * width / 4 + digitHeight * digitHeight / 4 <= room * room + 0.01f;
    }

    public static void Draw(HudPainter p, string text, float x, float y, float r, Color dot)
    {
        if (text.Length == 0) return;
        var (top, bottom) = DigitBand;
        float share = bottom - top;
        var reference = p.Font(ReferencePx, true);
        float textScale = reference.Size / ReferencePx;
        float px = FitPx(p.Measure(text, reference), share, r, textScale);
        Font? font = null;
        while (px >= SmallestPx)
        {
            font = p.Font(px, true);
            if (Fits(p.Measure(text, font), share * font.Size, r)) break;
            font = null;
            px -= 0.5f;
        }
        if (font is null) return;
        float textTop = y - (top + bottom) / 2 * font.Size;
        float h = p.LineHeight(font);
        p.Text(text, font, HudPalette.On(dot), new RectangleF(x - r * 2, textTop, r * 4, h), HAlign.Center, VAlign.Top, false);
    }
}
