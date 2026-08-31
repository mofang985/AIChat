using AIChat.RpaClient.Automation;

namespace AIChat.UnitTests;

public sealed class WeChatLayoutCandidateScorerTests
{
    [Fact]
    public void Score_ShouldPreferStableInputAreaCandidate()
    {
        var good = Candidate(
            inputTopY: 820,
            source: "HorizontalLine",
            horizontalCoverage: 0.82,
            rowContrast: 8.5,
            inputWhiteRatio: 0.88,
            inputHeightRatio: 0.24,
            chatHeightRatio: 0.61,
            messageLikeRegionCount: 4,
            hasSendButton: true,
            sendButtonAlignmentScore: 0.92);
        var messageEdge = Candidate(
            inputTopY: 520,
            source: "HorizontalLine",
            horizontalCoverage: 0.74,
            rowContrast: 7.0,
            inputWhiteRatio: 0.44,
            inputHeightRatio: 0.08,
            chatHeightRatio: 0.79,
            messageLikeRegionCount: 1,
            hasSendButton: false,
            sendButtonAlignmentScore: 0);

        var goodScore = WeChatLayoutCandidateScorer.Score(good, SafeGeometry(good));
        var edgeScore = WeChatLayoutCandidateScorer.Score(messageEdge, SafeGeometry(messageEdge));

        Assert.True(goodScore.IsAccepted(0.65m));
        Assert.False(edgeScore.IsAccepted(0.65m));
        Assert.True(goodScore.Confidence > edgeScore.Confidence);
    }

    [Fact]
    public void Score_ShouldRejectConversationContextOverlappingInputArea()
    {
        var candidate = Candidate(
            inputTopY: 820,
            source: "HorizontalLine",
            horizontalCoverage: 0.82,
            rowContrast: 8.5,
            inputWhiteRatio: 0.88,
            inputHeightRatio: 0.24,
            chatHeightRatio: 0.61,
            messageLikeRegionCount: 4,
            hasSendButton: true,
            sendButtonAlignmentScore: 0.92);
        var geometry = SafeGeometry(candidate) with
        {
            ChatContentBottomY = 860,
            ConversationContextBottomY = 850,
            ChatContentSeparatedFromInputArea = false
        };

        var score = WeChatLayoutCandidateScorer.Score(candidate, geometry);

        Assert.False(score.IsSafe);
        Assert.Contains("chat-input-overlap", score.FailureReasons);
        Assert.False(score.IsAccepted(0.65m));
    }

    [Fact]
    public void Score_ShouldRejectInputVerifyRegionOutsideInputArea()
    {
        var candidate = Candidate(
            inputTopY: 820,
            source: "HorizontalLine",
            horizontalCoverage: 0.82,
            rowContrast: 8.5,
            inputWhiteRatio: 0.88,
            inputHeightRatio: 0.24,
            chatHeightRatio: 0.61,
            messageLikeRegionCount: 4,
            hasSendButton: true,
            sendButtonAlignmentScore: 0.92);
        var geometry = SafeGeometry(candidate) with
        {
            InputVerifyTopY = 780,
            InputVerifyInsideInputArea = false
        };

        var score = WeChatLayoutCandidateScorer.Score(candidate, geometry);

        Assert.False(score.IsSafe);
        Assert.Contains("input-verify-outside-input-area", score.FailureReasons);
        Assert.False(score.IsAccepted(0.65m));
    }

    [Fact]
    public void Score_ShouldRejectLowConfidenceCandidateUnderAutoOnlyThreshold()
    {
        var weak = Candidate(
            inputTopY: 780,
            source: "FallbackRatio",
            horizontalCoverage: 0.20,
            rowContrast: 1.0,
            inputWhiteRatio: 0.62,
            inputHeightRatio: 0.26,
            chatHeightRatio: 0.55,
            messageLikeRegionCount: 1,
            hasSendButton: false,
            sendButtonAlignmentScore: 0);

        var score = WeChatLayoutCandidateScorer.Score(weak, SafeGeometry(weak));

        Assert.True(score.IsSafe);
        Assert.False(score.IsAccepted(0.65m));
    }

    [Fact]
    public void Score_ShouldAcceptCompactInputAreaWhenSignalsAreStrong()
    {
        var compactInput = Candidate(
            inputTopY: 1026,
            source: "HorizontalLine",
            horizontalCoverage: 1.00,
            rowContrast: 24.94,
            inputWhiteRatio: 0.99,
            inputHeightRatio: 0.09,
            chatHeightRatio: 0.84,
            messageLikeRegionCount: 10,
            hasSendButton: true,
            sendButtonAlignmentScore: 0.61);
        var geometry = SafeGeometry(compactInput, imageHeight: 1128) with
        {
            ChatContentBottomY = compactInput.InputTopY,
            ConversationContextBottomY = compactInput.InputTopY
        };

        var score = WeChatLayoutCandidateScorer.Score(compactInput, geometry);

        Assert.True(score.IsSafe);
        Assert.DoesNotContain("input-area-height-out-of-range", score.FailureReasons);
        Assert.DoesNotContain("chat-input-overlap", score.FailureReasons);
        Assert.True(score.IsAccepted(0.65m));
    }

    private static WeChatLayoutCandidate Candidate(
        int inputTopY,
        string source,
        double horizontalCoverage,
        double rowContrast,
        double inputWhiteRatio,
        double inputHeightRatio,
        double chatHeightRatio,
        int messageLikeRegionCount,
        bool hasSendButton,
        double sendButtonAlignmentScore)
    {
        return new WeChatLayoutCandidate(
            inputTopY,
            source,
            horizontalCoverage,
            rowContrast,
            inputWhiteRatio,
            inputHeightRatio,
            chatHeightRatio,
            messageLikeRegionCount,
            hasSendButton,
            sendButtonAlignmentScore);
    }

    private static WeChatLayoutCandidateGeometry SafeGeometry(WeChatLayoutCandidate candidate, int imageHeight = 1080)
    {
        var chatTop = 78;
        var contextBottom = Math.Max(chatTop + 1, candidate.InputTopY - 14);
        var inputBottom = imageHeight;
        var verifyTop = candidate.InputTopY + 16;
        var verifyBottom = Math.Min(inputBottom - 52, candidate.InputTopY + 180);

        return new WeChatLayoutCandidateGeometry(
            1920,
            imageHeight,
            chatTop,
            candidate.InputTopY - 12,
            candidate.InputTopY,
            inputBottom,
            chatTop + 50,
            contextBottom,
            verifyTop,
            verifyBottom,
            true,
            true,
            true);
    }
}
