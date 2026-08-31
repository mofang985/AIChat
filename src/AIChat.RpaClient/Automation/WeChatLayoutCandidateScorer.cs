namespace AIChat.RpaClient.Automation;

internal sealed record WeChatLayoutCandidate(
    int InputTopY,
    string Source,
    double HorizontalCoverage,
    double RowContrast,
    double InputWhiteRatio,
    double InputHeightRatio,
    double ChatHeightRatio,
    int MessageLikeRegionCount,
    bool HasSendButton,
    double SendButtonAlignmentScore)
{
    public string ToMetricSummary()
    {
        return
            $"y={InputTopY}; source={Source}; line={HorizontalCoverage:0.00}; contrast={RowContrast:0.00}; white={InputWhiteRatio:0.00}; inputH={InputHeightRatio:0.00}; chatH={ChatHeightRatio:0.00}; bubbles={MessageLikeRegionCount}; send={HasSendButton}; sendAlign={SendButtonAlignmentScore:0.00}";
    }
}

internal sealed record WeChatLayoutCandidateGeometry(
    int ImageWidth,
    int ImageHeight,
    int ChatContentTopY,
    int ChatContentBottomY,
    int InputAreaTopY,
    int InputAreaBottomY,
    int ConversationContextTopY,
    int ConversationContextBottomY,
    int InputVerifyTopY,
    int InputVerifyBottomY,
    bool InputVerifyInsideInputArea,
    bool ConversationContextInsideChatContent,
    bool ChatContentSeparatedFromInputArea);

internal sealed record WeChatLayoutCandidateScore(
    decimal Confidence,
    bool IsSafe,
    string Reason,
    IReadOnlyList<string> FailureReasons)
{
    public bool IsAccepted(decimal minConfidence)
    {
        return IsSafe && Confidence >= minConfidence;
    }
}

internal static class WeChatLayoutCandidateScorer
{
    public const double MinInputHeightRatio = 0.08d;
    public const double MaxInputHeightRatio = 0.38d;
    public const double MinChatHeightRatio = 0.30d;
    public const double MinHorizontalCoverage = 0.55d;

    public static WeChatLayoutCandidateScore Score(
        WeChatLayoutCandidate candidate,
        WeChatLayoutCandidateGeometry geometry)
    {
        var failures = Validate(candidate, geometry);
        var isSafe = failures.Count == 0;

        var lineScore = Clamp01((candidate.HorizontalCoverage - 0.35d) / 0.45d);
        var contrastScore = Clamp01((candidate.RowContrast - 2.0d) / 12.0d);
        var whiteScore = Clamp01((candidate.InputWhiteRatio - 0.58d) / 0.32d);
        var inputHeightScore = Clamp01(1d - Math.Abs(candidate.InputHeightRatio - 0.22d) / 0.14d);
        var chatHeightScore = Clamp01((candidate.ChatHeightRatio - MinChatHeightRatio) / 0.36d);
        var messageScore = Clamp01(candidate.MessageLikeRegionCount / 5d);
        var sendButtonScore = candidate.HasSendButton
            ? Clamp01(candidate.SendButtonAlignmentScore)
            : 0.40d;
        var sourceBonus = GetSourceBonus(candidate.Source);

        var confidence =
            0.08d +
            lineScore * 0.20d +
            contrastScore * 0.08d +
            whiteScore * 0.22d +
            inputHeightScore * 0.17d +
            chatHeightScore * 0.10d +
            sendButtonScore * 0.10d +
            messageScore * 0.05d +
            sourceBonus;

        if (candidate.Source.Contains("Fallback", StringComparison.OrdinalIgnoreCase))
        {
            confidence = Math.Min(confidence, 0.62d);
        }

        if (!isSafe)
        {
            confidence = Math.Min(confidence, 0.45d);
        }

        var normalized = (decimal)Math.Round(Clamp01(confidence), 4, MidpointRounding.AwayFromZero);
        var reason = failures.Count == 0
            ? $"score={normalized:0.0000}; {candidate.ToMetricSummary()}"
            : $"score={normalized:0.0000}; failures={string.Join(",", failures)}; {candidate.ToMetricSummary()}";

        return new WeChatLayoutCandidateScore(normalized, isSafe, reason, failures);
    }

    private static List<string> Validate(
        WeChatLayoutCandidate candidate,
        WeChatLayoutCandidateGeometry geometry)
    {
        var failures = new List<string>();
        if (candidate.InputTopY <= geometry.ChatContentTopY)
        {
            failures.Add("input-top-before-chat-content");
        }

        if (candidate.InputHeightRatio < MinInputHeightRatio ||
            candidate.InputHeightRatio > MaxInputHeightRatio)
        {
            failures.Add("input-area-height-out-of-range");
        }

        if (candidate.ChatHeightRatio < MinChatHeightRatio)
        {
            failures.Add("chat-content-too-small");
        }

        if (!geometry.ChatContentSeparatedFromInputArea ||
            geometry.ChatContentBottomY > geometry.InputAreaTopY)
        {
            failures.Add("chat-input-overlap");
        }

        if (!geometry.ConversationContextInsideChatContent ||
            geometry.ConversationContextBottomY > geometry.ChatContentBottomY ||
            geometry.ConversationContextBottomY > geometry.InputAreaTopY)
        {
            failures.Add("conversation-context-outside-chat-content");
        }

        if (!geometry.InputVerifyInsideInputArea ||
            geometry.InputVerifyTopY < geometry.InputAreaTopY ||
            geometry.InputVerifyBottomY > geometry.InputAreaBottomY)
        {
            failures.Add("input-verify-outside-input-area");
        }

        if (candidate.Source.Contains("HorizontalLine", StringComparison.OrdinalIgnoreCase) &&
            candidate.HorizontalCoverage < MinHorizontalCoverage)
        {
            failures.Add("horizontal-line-too-short");
        }

        if (geometry.InputAreaTopY < (int)(geometry.ImageHeight * 0.55d) ||
            geometry.InputAreaTopY > (int)(geometry.ImageHeight * 0.92d))
        {
            failures.Add("input-top-outside-search-range");
        }

        return failures;
    }

    private static double GetSourceBonus(string source)
    {
        if (source.Contains("HorizontalLine", StringComparison.OrdinalIgnoreCase))
        {
            return 0.04d;
        }

        if (source.Contains("WhiteArea", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("SendButton", StringComparison.OrdinalIgnoreCase))
        {
            return 0.03d;
        }

        return 0.01d;
    }

    private static double Clamp01(double value)
    {
        return Math.Clamp(value, 0d, 1d);
    }
}
