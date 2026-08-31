using System.Drawing;
using AIChat.RpaClient.Automation;

namespace AIChat.UnitTests;

public sealed class WeChatConversationListRegionPlannerTests
{
    [Fact]
    public void CreateLocalRegion_ShouldIncludeNavigationRailAndFullConversationList()
    {
        var region = WeChatConversationListRegionPlanner.CreateLocalRegion(
            imageWidth: 1568,
            imageHeight: 850,
            chatLeftX: 185);

        Assert.Equal(new Rectangle(33, 0, 152, 850), region);
    }

    [Fact]
    public void CreateLocalRegion_ShouldScaleNavigationRailForLargeDpiCapture()
    {
        var region = WeChatConversationListRegionPlanner.CreateLocalRegion(
            imageWidth: 3840,
            imageHeight: 2088,
            chatLeftX: 444);

        Assert.Equal(new Rectangle(80, 0, 364, 2088), region);
    }

    [Fact]
    public void CreateLocalRegion_ShouldClampNavigationRailBeforeChatPane()
    {
        var region = WeChatConversationListRegionPlanner.CreateLocalRegion(
            imageWidth: 1200,
            imageHeight: 900,
            chatLeftX: 24);

        Assert.Equal(new Rectangle(23, 0, 1, 900), region);
    }

    [Fact]
    public void CreateLocalRegion_ShouldReturnEmptyForInvalidInput()
    {
        Assert.Equal(Rectangle.Empty, WeChatConversationListRegionPlanner.CreateLocalRegion(1, 900, 200));
        Assert.Equal(Rectangle.Empty, WeChatConversationListRegionPlanner.CreateLocalRegion(1200, 0, 200));
        Assert.Equal(Rectangle.Empty, WeChatConversationListRegionPlanner.CreateLocalRegion(1200, 900, 1));
    }
}
