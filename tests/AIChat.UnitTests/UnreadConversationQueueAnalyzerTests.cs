using OpenCvSharp;
using System.Drawing;
using AIChat.RpaClient.Automation;

namespace AIChat.UnitTests;

public sealed class UnreadConversationQueueAnalyzerTests
{
    [Fact]
    public void BuildQueue_ShouldSortByVisualTopAndLimitCandidates()
    {
        var region = new Rectangle(100, 100, 278, 900);
        var detections = new[]
        {
            new UnreadBadgeDetection(new Rectangle(150, 360, 16, 16), 0.80m, "test"),
            new UnreadBadgeDetection(new Rectangle(150, 166, 12, 12), 0.70m, "test"),
            new UnreadBadgeDetection(new Rectangle(150, 230, 34, 16), 0.90m, "test")
        };

        var candidates = UnreadConversationQueueAnalyzer.BuildQueue(region, detections, maxCandidates: 2);

        Assert.Equal(2, candidates.Count);
        Assert.Equal(new Rectangle(150, 166, 12, 12), candidates[0].BadgeBounds);
        Assert.Equal(new Rectangle(150, 230, 34, 16), candidates[1].BadgeBounds);
        Assert.Equal(0, candidates[0].VisualOrder);
        Assert.Equal(1, candidates[1].VisualOrder);
        Assert.Equal(region.Left, candidates[0].RowBounds.Left);
        Assert.Equal(region.Width, candidates[0].RowBounds.Width);
        Assert.Contains("多位数字未读候选", candidates[1].ToDisplayText(), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildQueue_ShouldKeepBestBadgePerConversationRow()
    {
        var region = new Rectangle(0, 0, 278, 900);
        var detections = new[]
        {
            new UnreadBadgeDetection(new Rectangle(47, 67, 10, 10), 0.60m, "small"),
            new UnreadBadgeDetection(new Rectangle(49, 69, 18, 18), 0.60m, "larger"),
            new UnreadBadgeDetection(new Rectangle(49, 135, 12, 12), 0.75m, "next-row")
        };

        var candidates = UnreadConversationQueueAnalyzer.BuildQueue(region, detections, maxCandidates: 8);

        Assert.Equal(2, candidates.Count);
        Assert.Equal(new Rectangle(49, 69, 18, 18), candidates[0].BadgeBounds);
        Assert.Equal("larger", candidates[0].Source);
        Assert.Equal(new Rectangle(49, 135, 12, 12), candidates[1].BadgeBounds);
    }

    [Fact]
    public void BuildQueue_ShouldIgnoreEmptyInputAndNonPositiveConfidence()
    {
        var region = new Rectangle(0, 0, 280, 640);
        var detections = new[]
        {
            new UnreadBadgeDetection(new Rectangle(180, 148, 10, 10), 0m, "zero"),
            new UnreadBadgeDetection(new Rectangle(180, 228, 10, 10), -0.10m, "negative")
        };

        Assert.Empty(UnreadConversationQueueAnalyzer.BuildQueue(Rectangle.Empty, detections, maxCandidates: 8));
        Assert.Empty(UnreadConversationQueueAnalyzer.BuildQueue(region, detections, maxCandidates: 8));
        Assert.Empty(UnreadConversationQueueAnalyzer.BuildQueue(region, detections, maxCandidates: 0));
    }

    [Fact]
    public void BuildQueue_ShouldIgnoreBadgesWithoutNumberGlyph()
    {
        var region = new Rectangle(0, 0, 272, 1568);
        var candidates = UnreadConversationQueueAnalyzer.BuildQueue(
            region,
            [
                new UnreadBadgeDetection(new Rectangle(53, 95, 18, 18), 0.86m, "red-dot", ContainsNumberGlyph: false),
                new UnreadBadgeDetection(new Rectangle(53, 535, 18, 18), 0.87m, "number", ContainsNumberGlyph: true)
            ],
            maxCandidates: 8);

        var candidate = Assert.Single(candidates);
        Assert.Equal(new Rectangle(53, 535, 18, 18), candidate.BadgeBounds);
        Assert.Equal("数字未读候选", candidate.UnreadHint);
    }

    [Fact]
    public void ContainsNumberGlyph_ShouldRequireWhiteGlyphInsideRedBadge()
    {
        using var redDot = CreateBadgeImage(withNumber: false);
        using var numberBadge = CreateBadgeImage(withNumber: true);

        Assert.False(UnreadNumberGlyphDetector.ContainsNumberGlyph(redDot, new OpenCvSharp.Rect(4, 4, 18, 18)));
        Assert.True(UnreadNumberGlyphDetector.ContainsNumberGlyph(numberBadge, new OpenCvSharp.Rect(4, 4, 18, 18)));
    }

    [Fact]
    public void LooksLikeUnreadBadgeLocal_ShouldRejectRedTextAndAvatarDecoration()
    {
        var imageWidth = 278;
        var imageHeight = 1568;

        Assert.True(UnreadConversationListGeometry.LooksLikeUnreadBadgeLocal(new Rectangle(49, 90, 18, 18), imageWidth, imageHeight));
        Assert.False(UnreadConversationListGeometry.LooksLikeUnreadBadgeLocal(new Rectangle(9, 92, 26, 20), imageWidth, imageHeight));
        Assert.False(UnreadConversationListGeometry.LooksLikeUnreadBadgeLocal(new Rectangle(118, 90, 20, 18), imageWidth, imageHeight));
        Assert.False(UnreadConversationListGeometry.LooksLikeUnreadBadgeLocal(new Rectangle(180, 548, 52, 18), imageWidth, imageHeight));
        Assert.False(UnreadConversationListGeometry.LooksLikeUnreadBadgeLocal(new Rectangle(25, 32, 18, 18), imageWidth, imageHeight));
    }

    [Fact]
    public void LooksLikeUnreadBadgeLocal_ShouldAcceptMultipleBadgesAcrossVisibleRows()
    {
        var imageWidth = 272;
        var imageHeight = 1568;

        var badgeBounds = new[]
        {
            new Rectangle(53, 95, 18, 18),
            new Rectangle(52, 535, 18, 18),
            new Rectangle(53, 690, 18, 18),
            new Rectangle(53, 839, 18, 18),
            new Rectangle(53, 924, 18, 18),
            new Rectangle(53, 1274, 18, 18)
        };

        Assert.All(badgeBounds, bounds => Assert.True(UnreadConversationListGeometry.LooksLikeUnreadBadgeLocal(bounds, imageWidth, imageHeight)));
    }

    [Fact]
    public void BuildQueue_ShouldReturnOnlyNumberUnreadRowsFromVisibleList()
    {
        var region = new Rectangle(0, 0, 272, 1568);
        var candidates = UnreadConversationQueueAnalyzer.BuildQueue(
            region,
            [
                new UnreadBadgeDetection(new Rectangle(53, 95, 18, 18), 0.86m, "red-dot", ContainsNumberGlyph: false),
                new UnreadBadgeDetection(new Rectangle(52, 535, 18, 18), 0.87m, "number", ContainsNumberGlyph: true),
                new UnreadBadgeDetection(new Rectangle(53, 690, 18, 18), 0.88m, "number", ContainsNumberGlyph: true),
                new UnreadBadgeDetection(new Rectangle(53, 839, 18, 18), 0.89m, "red-dot", ContainsNumberGlyph: false),
                new UnreadBadgeDetection(new Rectangle(53, 924, 18, 18), 0.90m, "number", ContainsNumberGlyph: true),
                new UnreadBadgeDetection(new Rectangle(53, 1274, 18, 18), 0.91m, "number", ContainsNumberGlyph: true)
            ],
            maxCandidates: 8);

        Assert.Equal(4, candidates.Count);
        Assert.Equal(new Rectangle(52, 535, 18, 18), candidates[0].BadgeBounds);
        Assert.Equal(new Rectangle(53, 1274, 18, 18), candidates[^1].BadgeBounds);
    }

    [Fact]
    public void BuildQueue_ShouldAlignRowBoundsToConversationRows()
    {
        var region = new Rectangle(0, 0, 278, 1568);
        var candidates = UnreadConversationQueueAnalyzer.BuildQueue(
            region,
            [new UnreadBadgeDetection(new Rectangle(49, 90, 18, 18), 0.85m, "test")],
            maxCandidates: 8);

        var candidate = Assert.Single(candidates);
        Assert.Equal(new Rectangle(0, 71, 278, 86), candidate.RowBounds);
    }
    private static Mat CreateBadgeImage(bool withNumber)
    {
        var image = new Mat(new OpenCvSharp.Size(26, 26), MatType.CV_8UC3, Scalar.All(0));
        Cv2.Circle(image, new OpenCvSharp.Point(13, 13), 9, new Scalar(0, 0, 255), -1);
        if (withNumber)
        {
            Cv2.PutText(
                image,
                "1",
                new OpenCvSharp.Point(9, 18),
                HersheyFonts.HersheySimplex,
                0.42,
                new Scalar(255, 255, 255),
                1,
                LineTypes.AntiAlias);
        }

        return image;
    }
}
