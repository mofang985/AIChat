using OpenCvSharp;

namespace AIChat.RpaClient.Automation;

internal static class UnreadNumberGlyphDetector
{
    public static bool ContainsNumberGlyph(Mat image, Rect badgeRect)
    {
        var safeRect = IntersectImage(badgeRect, image.Width, image.Height);
        if (safeRect.Width <= 0 || safeRect.Height <= 0)
        {
            return false;
        }

        var marginX = Math.Clamp(safeRect.Width / 5, 1, 5);
        var marginY = Math.Clamp(safeRect.Height / 5, 1, 5);
        var left = safeRect.Left + marginX;
        var top = safeRect.Top + marginY;
        var right = safeRect.Right - marginX;
        var bottom = safeRect.Bottom - marginY;
        if (right <= left || bottom <= top)
        {
            return false;
        }

        var lightPixels = 0;
        var minX = right;
        var minY = bottom;
        var maxX = left;
        var maxY = top;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var pixel = image.At<Vec3b>(y, x);
                if (!IsWhiteDigitPixel(pixel))
                {
                    continue;
                }

                lightPixels++;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        var minimumLightPixels = Math.Max(
            5,
            (int)Math.Round(safeRect.Width * safeRect.Height * 0.018d, MidpointRounding.AwayFromZero));
        if (lightPixels < minimumLightPixels)
        {
            return false;
        }

        var glyphWidth = maxX - minX + 1;
        var glyphHeight = maxY - minY + 1;
        return glyphHeight >= Math.Max(4, safeRect.Height / 4) &&
            glyphWidth <= Math.Max(4, safeRect.Width * 3 / 4);
    }

    private static bool IsWhiteDigitPixel(Vec3b pixel)
    {
        var blue = pixel.Item0;
        var green = pixel.Item1;
        var red = pixel.Item2;
        return blue >= 185 && green >= 185 && red >= 185 &&
            Math.Abs(red - green) <= 70 &&
            Math.Abs(red - blue) <= 70 &&
            Math.Abs(green - blue) <= 70;
    }

    private static Rect IntersectImage(Rect rect, int imageWidth, int imageHeight)
    {
        var left = Math.Clamp(rect.Left, 0, imageWidth);
        var top = Math.Clamp(rect.Top, 0, imageHeight);
        var right = Math.Clamp(rect.Right, left, imageWidth);
        var bottom = Math.Clamp(rect.Bottom, top, imageHeight);
        return new Rect(left, top, right - left, bottom - top);
    }
}
