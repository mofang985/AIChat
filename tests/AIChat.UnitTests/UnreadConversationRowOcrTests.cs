using System.Drawing;
using AIChat.RpaClient.Automation;

namespace AIChat.UnitTests;

public sealed class UnreadConversationRowOcrTests
{
    [Fact]
    public void CreateRegions_ShouldSplitNamePreviewTimeAndBadgeAreas()
    {
        var rowBounds = new Rectangle(0, 516, 272, 84);
        var badgeBounds = new Rectangle(52, 535, 18, 18);

        var regions = UnreadConversationRowOcrPlanner.CreateRegions(rowBounds, badgeBounds);

        Assert.Equal(new Rectangle(68, 526, 125, 27), regions.NameRegion);
        Assert.Equal(new Rectangle(68, 555, 196, 27), regions.PreviewRegion);
        Assert.Equal(new Rectangle(197, 531, 71, 24), regions.TimeRegion);
        Assert.Equal(new Rectangle(45, 528, 32, 32), regions.BadgeRegion);
    }

    [Fact]
    public void CreateTextRegion_ShouldCoverNamePreviewAndTimeInOneCrop()
    {
        var rowBounds = new Rectangle(0, 516, 272, 84);

        var region = UnreadConversationRowOcrPlanner.CreateTextRegion(rowBounds);

        Assert.Equal(new Rectangle(60, 516, 208, 84), region);
    }

    [Fact]
    public void BuildInfo_ShouldNormalizeReadableQueueFields()
    {
        var info = UnreadConversationRowOcrParser.BuildInfo(
            new OcrResult("是章鱼呀小号\r\n噪声", 0.92m, "NameOcr"),
            new OcrResult("22", 0.88m, "PreviewOcr"),
            new OcrResult("16：13", 0.90m, "TimeOcr"),
            new OcrResult("I", 0.80m, "BadgeOcr"));

        Assert.Equal("是章鱼呀小号", info.ConversationName);
        Assert.Equal("22", info.LatestMessagePreview);
        Assert.Equal("16:13", info.TimeText);
        Assert.Equal("1", info.UnreadCountText);
        Assert.Equal(0.875m, info.Confidence);
        Assert.Contains("NameOcr", info.Source, StringComparison.Ordinal);
        Assert.Equal("是章鱼呀小号｜未读 1｜22｜16:13", info.ToDisplayText());
    }

    [Fact]
    public void BuildInfoFromRow_ShouldParseWholeRowOcrText()
    {
        var info = UnreadConversationRowOcrParser.BuildInfoFromRow(new OcrResult("贺志奇\r\n还特别的吗\r\n15:32", 0.90m, "WindowsOcrUi"));

        Assert.Equal("贺志奇", info.ConversationName);
        Assert.Equal("还特别的吗", info.LatestMessagePreview);
        Assert.Equal("15:32", info.TimeText);
        Assert.Equal(string.Empty, info.UnreadCountText);
        Assert.Equal("贺志奇｜未读 数字｜还特别的吗｜15:32", info.ToDisplayText());
    }

    [Theory]
    [InlineData("2", "2")]
    [InlineData("10+", "10+")]
    [InlineData("O", "0")]
    [InlineData("l", "1")]
    [InlineData("无数字", "")]
    public void NormalizeUnreadCount_ShouldReturnDigitTextOnly(string input, string expected)
    {
        Assert.Equal(expected, UnreadConversationRowOcrParser.NormalizeUnreadCount(input));
    }

    [Theory]
    [InlineData("16：13", "16:13")]
    [InlineData("星期一", "星期一")]
    [InlineData("周日", "周日")]
    [InlineData("不是时间", "")]
    public void NormalizeTimeText_ShouldReturnWechatTimeTextOnly(string input, string expected)
    {
        Assert.Equal(expected, UnreadConversationRowOcrParser.NormalizeTimeText(input));
    }

    [Fact]
    public void CandidateDisplay_ShouldPreferReadableTextInfo()
    {
        var info = new UnreadConversationTextInfo("微信支付", "[2条] 已扣费21.37", "12:56", "2", 0.85m, "TestOcr", "raw");
        var candidate = new UnreadConversationCandidate(
            VisualOrder: 0,
            BadgeBounds: new Rectangle(53, 1274, 18, 18),
            RowBounds: new Rectangle(0, 1248, 272, 84),
            UnreadHint: "数字未读候选",
            Confidence: 0.91m,
            Source: "NumberBadgeOpenCv",
            TextInfo: info);

        Assert.Equal("#1 微信支付｜未读 2｜[2条] 已扣费21.37｜12:56 / 角标置信度 91% / OCR 85%", candidate.ToDisplayText());
    }
}
