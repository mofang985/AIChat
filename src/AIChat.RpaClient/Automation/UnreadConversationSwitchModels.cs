using System.Drawing;
using System.Text.RegularExpressions;

namespace AIChat.RpaClient.Automation;

public sealed record UnreadConversationSwitchResult(
    bool IsSuccess,
    string Status,
    string Reason,
    UnreadConversationCandidate Target,
    Point ClickPoint,
    Rectangle TitleRegion,
    string TitleOcrText,
    UnreadConversationMessageVerification? MessageVerification = null)
{
    public string ToLogMessage()
    {
        var title = string.IsNullOrWhiteSpace(TitleOcrText) ? "未识别" : TitleOcrText.Replace(Environment.NewLine, " ");
        var message = $"{Status}：{Reason}，点击点 X={ClickPoint.X},Y={ClickPoint.Y}，标题 OCR={title}。";
        return MessageVerification is null ? message : $"{message} 摘要校验={MessageVerification.ToDisplayText()}。";
    }
}

public sealed record UnreadConversationMessageVerification(
    bool IsVerified,
    bool IsSkipped,
    string Status,
    string Reason,
    string QueuePreview,
    string MatchedText,
    string Source,
    string? DebugCapturePath)
{
    public bool IsBlockingFailure => !IsVerified && !IsSkipped;

    public string ToDisplayText()
    {
        var queuePreview = string.IsNullOrWhiteSpace(QueuePreview) ? "无队列摘要" : QueuePreview.Replace(Environment.NewLine, " ");
        var matched = string.IsNullOrWhiteSpace(MatchedText) ? "无匹配消息" : MatchedText.Replace(Environment.NewLine, " ");
        var debug = string.IsNullOrWhiteSpace(DebugCapturePath) ? string.Empty : $"，消息截图：{DebugCapturePath}";
        return $"{Status}：{Reason}，队列摘要={queuePreview}，匹配消息={matched}{debug}";
    }
}

internal static partial class UnreadConversationMessageVerifier
{
    private static readonly Regex LeadingUnreadCountRegex = new(@"^[\[【]\s*\d+\s*条\s*[\]】]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool HasComparableQueuePreview(UnreadConversationCandidate target, int minComparableChars)
    {
        var minimumLength = Math.Max(2, minComparableChars);
        return NormalizeComparablePreview(target.TextInfo?.LatestMessagePreview).Length >= minimumLength;
    }

    public static UnreadConversationMessageVerification Verify(
        UnreadConversationCandidate target,
        ChatMessageVisualExtractionResult visualMessages,
        int minComparableChars)
    {
        var queuePreview = target.TextInfo?.LatestMessagePreview ?? string.Empty;
        var comparablePreview = NormalizeComparablePreview(queuePreview);
        var minimumLength = Math.Max(2, minComparableChars);
        if (comparablePreview.Length < minimumLength)
        {
            return CreateSkipped(queuePreview, visualMessages.DebugCapturePath, visualMessages.Source, "队列摘要缺少可比较文本");
        }

        var candidateMessages = visualMessages.Messages
            .Where(message => message.SenderType != ChatMessageSenderType.System)
            .Where(message => message.HasText)
            .Where(message => !CustomerMessageExtractor.IsWeChatSystemNotice(message.Text))
            .Where(message => !LooksLikeUiNoise(message.Text))
            .OrderByDescending(message => message.VisualOrder)
            .ThenByDescending(message => message.Bounds.Bottom)
            .Take(8)
            .ToArray();
        var latestWindowCount = Math.Min(3, candidateMessages.Length);
        for (var index = 0; index < latestWindowCount; index++)
        {
            var message = candidateMessages[index];
            if (LooksLikePreviewMatch(comparablePreview, message.Text, minimumLength))
            {
                return new UnreadConversationMessageVerification(
                    true,
                    false,
                    "摘要校验通过",
                    index == 0 ? "右侧最新可见消息包含队列摘要" : "右侧底部最新消息组包含队列摘要",
                    queuePreview,
                    message.Text,
                    visualMessages.Source,
                    visualMessages.DebugCapturePath);
            }
        }

        for (var index = latestWindowCount; index < candidateMessages.Length; index++)
        {
            var message = candidateMessages[index];
            if (LooksLikePreviewMatch(comparablePreview, message.Text, minimumLength))
            {
                return new UnreadConversationMessageVerification(
                    false,
                    false,
                    "摘要校验失败",
                    "仅较早可见消息包含队列摘要，右侧最新消息不一致",
                    queuePreview,
                    message.Text,
                    visualMessages.Source,
                    visualMessages.DebugCapturePath);
            }
        }

        var latestText = candidateMessages.FirstOrDefault()?.Text ?? string.Empty;
        var reason = string.IsNullOrWhiteSpace(latestText)
            ? "右侧未识别到可比较消息"
            : "右侧最新消息与队列摘要不一致";
        return new UnreadConversationMessageVerification(
            false,
            false,
            "摘要校验失败",
            reason,
            queuePreview,
            latestText,
            visualMessages.Source,
            visualMessages.DebugCapturePath);
    }

    private static UnreadConversationMessageVerification CreateSkipped(string queuePreview, string? debugCapturePath, string source, string reason)
    {
        return new UnreadConversationMessageVerification(
            false,
            true,
            "摘要校验跳过",
            reason,
            queuePreview,
            string.Empty,
            source,
            debugCapturePath);
    }

    private static bool LooksLikePreviewMatch(string comparablePreview, string messageText, int minimumLength)
    {
        var comparableMessage = NormalizeComparablePreview(messageText);
        if (comparableMessage.Length < minimumLength)
        {
            return false;
        }

        return comparableMessage.Contains(comparablePreview, StringComparison.Ordinal) ||
            comparablePreview.Contains(comparableMessage, StringComparison.Ordinal);
    }

    private static bool LooksLikeUiNoise(string text)
    {
        var normalized = NormalizeComparablePreview(text);
        if (normalized.Length == 0)
        {
            return true;
        }

        if (normalized.Length is 3 or 4 && normalized.All(char.IsDigit))
        {
            return true;
        }

        return normalized is "今天" or "昨天";
    }

    private static string NormalizeComparablePreview(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var withoutCount = LeadingUnreadCountRegex.Replace(value.Trim(), string.Empty);
        var withoutEllipsis = withoutCount
            .Replace("...", string.Empty, StringComparison.Ordinal)
            .Replace("…", string.Empty, StringComparison.Ordinal)
            .Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal)
            .Replace("【", string.Empty, StringComparison.Ordinal)
            .Replace("】", string.Empty, StringComparison.Ordinal);
        return CustomerMessageExtractor.NormalizeForComparison(withoutEllipsis);
    }
}

public sealed record UnreadConversationSwitchValidation(bool IsAllowed, string Reason)
{
    public static UnreadConversationSwitchValidation Allowed(string reason)
    {
        return new UnreadConversationSwitchValidation(true, reason);
    }

    public static UnreadConversationSwitchValidation Blocked(string reason)
    {
        return new UnreadConversationSwitchValidation(false, reason);
    }
}

internal static class UnreadConversationSwitchPlanner
{
    public static UnreadConversationCandidate? FindFirstSwitchableCandidate(UnreadConversationQueueSnapshot snapshot)
    {
        return snapshot.Candidates
            .Where(candidate => ValidateSwitchTarget(candidate, snapshot.Region).IsAllowed)
            .OrderBy(candidate => candidate.VisualOrder)
            .FirstOrDefault();
    }

    public static UnreadConversationSwitchValidation ValidateSwitchTarget(UnreadConversationCandidate candidate, Rectangle conversationListRegion)
    {
        if (candidate.Preflight?.IsStable != true)
        {
            return UnreadConversationSwitchValidation.Blocked("候选尚未通过连续扫描稳定性预演。");
        }

        if (candidate.TextInfo is not { HasAnyText: true } || string.IsNullOrWhiteSpace(candidate.TextInfo.ConversationName))
        {
            return UnreadConversationSwitchValidation.Blocked("候选缺少可靠会话名，不能点击切换。");
        }

        if (conversationListRegion.IsEmpty || !conversationListRegion.Contains(candidate.RowBounds))
        {
            return UnreadConversationSwitchValidation.Blocked("候选行不在当前会话列表区域内。");
        }

        return UnreadConversationSwitchValidation.Allowed("候选通过受控切换前置校验。");
    }

    public static Point CreateClickPoint(Rectangle rowBounds)
    {
        var x = rowBounds.Left + Math.Clamp(
            (int)Math.Round(rowBounds.Width * 0.52d, MidpointRounding.AwayFromZero),
            24,
            Math.Max(24, rowBounds.Width - 18));
        var y = rowBounds.Top + Math.Clamp(rowBounds.Height / 2, 10, Math.Max(10, rowBounds.Height - 10));
        return new Point(x, y);
    }

    public static Rectangle CreateTitleRegion(Rectangle clientBounds, Rectangle chatContentRegion)
    {
        if (clientBounds.IsEmpty || chatContentRegion.IsEmpty)
        {
            return Rectangle.Empty;
        }

        var left = chatContentRegion.Left + Math.Clamp(chatContentRegion.Width / 80, 8, 22);
        var top = clientBounds.Top;
        var headerHeight = Math.Clamp(chatContentRegion.Top - clientBounds.Top, 56, 112);
        var width = Math.Clamp(chatContentRegion.Width / 2, 260, 720);
        return Rectangle.Intersect(clientBounds, new Rectangle(left, top, width, headerHeight));
    }

    public static bool TitleMatchesTarget(string? titleOcrText, string? targetConversationName)
    {
        var title = NormalizeForMatch(titleOcrText);
        var target = NormalizeForMatch(targetConversationName);
        if (title.Length < 2 || target.Length < 2)
        {
            return false;
        }

        if (title.Contains(target, StringComparison.Ordinal) || target.Contains(title, StringComparison.Ordinal))
        {
            return true;
        }

        var prefixLength = Math.Min(4, target.Length);
        return prefixLength >= 2 && title.Contains(target[..prefixLength], StringComparison.Ordinal);
    }

    public static Rectangle CreateMessageVerifyRegion(Rectangle conversationContextRegion, decimal bottomRatio)
    {
        if (conversationContextRegion.IsEmpty || conversationContextRegion.Width <= 0 || conversationContextRegion.Height <= 0)
        {
            return Rectangle.Empty;
        }

        var ratio = Math.Clamp((double)bottomRatio, 0.20d, 1.00d);
        var desiredHeight = (int)Math.Round(conversationContextRegion.Height * ratio, MidpointRounding.AwayFromZero);
        var minHeight = Math.Min(conversationContextRegion.Height, 160);
        var height = Math.Clamp(desiredHeight, minHeight, conversationContextRegion.Height);
        return new Rectangle(
            conversationContextRegion.Left,
            conversationContextRegion.Bottom - height,
            conversationContextRegion.Width,
            height);
    }

    public static bool LooksSelectedConversationRow(int width, int height, Func<int, int, (byte R, byte G, byte B)> getPixel)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var startY = Math.Clamp(height / 8, 0, height - 1);
        var endY = Math.Clamp(height - height / 8, startY + 1, height);
        var greenPixels = 0;
        var totalPixels = 0;
        for (var y = startY; y < endY; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (red, green, blue) = getPixel(x, y);
                totalPixels++;
                if (green >= 135 && red <= 90 && blue <= 170 && green - red >= 55 && green - blue >= 20)
                {
                    greenPixels++;
                }
            }
        }

        return totalPixels > 0 && greenPixels / (double)totalPixels >= 0.22d;
    }

    private static string NormalizeForMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value
            .Where(ch => !char.IsWhiteSpace(ch))
            .Where(ch => ch is not '…' and not '.' and not '。' and not ':' and not '：' and not '-' and not '_' and not '|')
            .ToArray();
        return new string(chars);
    }
}
