using System.Drawing;

namespace AIChat.RpaClient.Automation;

internal static class WeChatConversationListRegionPlanner
{
    private const double NavigationRailWidthRatio = 48d;
    private const int MinimumNavigationRailWidth = 32;
    private const int MaximumNavigationRailWidth = 88;

    public static Rectangle CreateLocalRegion(int imageWidth, int imageHeight, int chatLeftX)
    {
        if (imageWidth <= 1 || imageHeight <= 0 || chatLeftX <= 1)
        {
            return Rectangle.Empty;
        }

        var chatLeft = Math.Clamp(chatLeftX, 1, imageWidth);
        var listLeft = Math.Min(EstimateListLeft(imageWidth), chatLeft - 1);
        return new Rectangle(listLeft, 0, chatLeft - listLeft, imageHeight);
    }

    private static int EstimateListLeft(int imageWidth)
    {
        return Math.Clamp(
            (int)Math.Round(imageWidth / NavigationRailWidthRatio, MidpointRounding.AwayFromZero),
            MinimumNavigationRailWidth,
            MaximumNavigationRailWidth);
    }
}
