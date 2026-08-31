using System.Drawing;

namespace AIChat.RpaClient.Automation;

public static class CustomerMessageOcrScanPlanner
{
    public static IReadOnlyList<Rectangle> CreateBottomUpScanRegions(Rectangle fullRegion)
    {
        if (fullRegion.Width <= 0 || fullRegion.Height <= 0)
        {
            return [];
        }

        var regions = new List<Rectangle>();
        var bandHeight = Math.Clamp(fullRegion.Height / 4, 180, 420);
        var step = Math.Max(80, bandHeight / 2);

        for (var bottom = fullRegion.Bottom; bottom > fullRegion.Top; bottom -= step)
        {
            var top = Math.Max(fullRegion.Top, bottom - bandHeight);
            var height = bottom - top;
            if (height < 80)
            {
                continue;
            }

            var region = new Rectangle(fullRegion.Left, top, fullRegion.Width, height);
            if (!regions.Any(existing => IsNearlySame(existing, region)))
            {
                regions.Add(region);
            }
        }

        if (!regions.Any(existing => IsNearlySame(existing, fullRegion)))
        {
            regions.Add(fullRegion);
        }

        return regions;
    }

    private static bool IsNearlySame(Rectangle first, Rectangle second)
    {
        return Math.Abs(first.Top - second.Top) <= 8 &&
            Math.Abs(first.Bottom - second.Bottom) <= 8 &&
            Math.Abs(first.Left - second.Left) <= 8 &&
            Math.Abs(first.Right - second.Right) <= 8;
    }
}
