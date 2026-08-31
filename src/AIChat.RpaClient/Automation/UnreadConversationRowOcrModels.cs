using System.Drawing;
using System.Text.RegularExpressions;

namespace AIChat.RpaClient.Automation;

public sealed record UnreadConversationTextInfo(
    string ConversationName,
    string LatestMessagePreview,
    string TimeText,
    string UnreadCountText,
    decimal Confidence,
    string Source,
    string RawText)
{
    public bool HasAnyText =>
        !string.IsNullOrWhiteSpace(ConversationName) ||
        !string.IsNullOrWhiteSpace(LatestMessagePreview) ||
        !string.IsNullOrWhiteSpace(TimeText) ||
        !string.IsNullOrWhiteSpace(UnreadCountText);

    public string ToDisplayText()
    {
        var name = string.IsNullOrWhiteSpace(ConversationName) ? "未命名会话" : ConversationName;
        var count = string.IsNullOrWhiteSpace(UnreadCountText) ? "数字" : UnreadCountText;
        var preview = string.IsNullOrWhiteSpace(LatestMessagePreview) ? "无摘要" : LatestMessagePreview;
        var time = string.IsNullOrWhiteSpace(TimeText) ? "无时间" : TimeText;
        return $"{name}｜未读 {count}｜{preview}｜{time}";
    }
}

public sealed record UnreadConversationRowOcrRegions(
    Rectangle NameRegion,
    Rectangle PreviewRegion,
    Rectangle TimeRegion,
    Rectangle BadgeRegion);

internal static class UnreadConversationRowOcrPlanner
{
    public static UnreadConversationRowOcrRegions CreateRegions(Rectangle rowBounds, Rectangle badgeBounds)
    {
        if (rowBounds.IsEmpty || rowBounds.Width <= 0 || rowBounds.Height <= 0)
        {
            return new UnreadConversationRowOcrRegions(Rectangle.Empty, Rectangle.Empty, Rectangle.Empty, Rectangle.Empty);
        }

        var textLeft = rowBounds.Left + Math.Clamp((int)Math.Round(rowBounds.Width * 0.25d, MidpointRounding.AwayFromZero), 58, 92);
        var timeWidth = Math.Clamp((int)Math.Round(rowBounds.Width * 0.22d, MidpointRounding.AwayFromZero), 48, 78);
        var timeLeft = Math.Max(textLeft + 32, rowBounds.Right - timeWidth - Math.Clamp(rowBounds.Width / 18, 6, 18));
        var nameTop = rowBounds.Top + Math.Clamp((int)Math.Round(rowBounds.Height * 0.12d, MidpointRounding.AwayFromZero), 8, 18);
        var nameHeight = Math.Clamp((int)Math.Round(rowBounds.Height * 0.32d, MidpointRounding.AwayFromZero), 20, 34);
        var previewTop = rowBounds.Top + Math.Clamp((int)Math.Round(rowBounds.Height * 0.46d, MidpointRounding.AwayFromZero), 30, 52);
        var previewHeight = Math.Clamp((int)Math.Round(rowBounds.Height * 0.32d, MidpointRounding.AwayFromZero), 20, 34);
        var timeTop = rowBounds.Top + Math.Clamp((int)Math.Round(rowBounds.Height * 0.18d, MidpointRounding.AwayFromZero), 10, 22);
        var timeHeight = Math.Clamp((int)Math.Round(rowBounds.Height * 0.28d, MidpointRounding.AwayFromZero), 18, 30);

        var nameRight = Math.Max(textLeft + 1, timeLeft - 4);
        var nameRegion = ClampTo(rowBounds, new Rectangle(textLeft, nameTop, nameRight - textLeft, nameHeight));
        var previewRegion = ClampTo(rowBounds, new Rectangle(textLeft, previewTop, rowBounds.Right - textLeft - 8, previewHeight));
        var timeRegion = ClampTo(rowBounds, new Rectangle(timeLeft, timeTop, rowBounds.Right - timeLeft - 4, timeHeight));
        var badgeRegion = badgeBounds.IsEmpty
            ? Rectangle.Empty
            : ClampTo(rowBounds, Expand(badgeBounds, Math.Clamp(rowBounds.Height / 12, 4, 8)));

        return new UnreadConversationRowOcrRegions(nameRegion, previewRegion, timeRegion, badgeRegion);
    }

    public static Rectangle CreateTextRegion(Rectangle rowBounds)
    {
        if (rowBounds.IsEmpty || rowBounds.Width <= 0 || rowBounds.Height <= 0)
        {
            return Rectangle.Empty;
        }

        var textLeft = rowBounds.Left + Math.Clamp((int)Math.Round(rowBounds.Width * 0.22d, MidpointRounding.AwayFromZero), 54, 86);
        return ClampTo(rowBounds, new Rectangle(textLeft, rowBounds.Top, rowBounds.Right - textLeft - 4, rowBounds.Height));
    }

    private static Rectangle Expand(Rectangle rectangle, int padding)
    {
        return new Rectangle(rectangle.Left - padding, rectangle.Top - padding, rectangle.Width + padding * 2, rectangle.Height + padding * 2);
    }

    private static Rectangle ClampTo(Rectangle bounds, Rectangle rectangle)
    {
        var left = Math.Clamp(rectangle.Left, bounds.Left, bounds.Right);
        var top = Math.Clamp(rectangle.Top, bounds.Top, bounds.Bottom);
        var right = Math.Clamp(rectangle.Right, left, bounds.Right);
        var bottom = Math.Clamp(rectangle.Bottom, top, bounds.Bottom);
        return new Rectangle(left, top, right - left, bottom - top);
    }
}

public static partial class UnreadConversationRowOcrParser
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex NumberRegex = new(@"\d+\+?", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TimeRegex = new(@"(\d{1,2}[:：]\d{2}|\d{1,2}/\d{1,2}|\d{1,2}-\d{1,2}|昨天|星期[一二三四五六日天]|周[一二三四五六日天])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static UnreadConversationTextInfo BuildInfo(
        OcrResult nameResult,
        OcrResult previewResult,
        OcrResult timeResult,
        OcrResult badgeResult)
    {
        var name = FirstLine(nameResult.Text);
        var preview = FirstLine(previewResult.Text);
        var time = NormalizeTimeText(timeResult.Text);
        var unreadCount = NormalizeUnreadCount(badgeResult.Text);
        var rawParts = new[] { nameResult.Text, previewResult.Text, timeResult.Text, badgeResult.Text }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim());
        var confidence = AverageConfidence(nameResult, previewResult, timeResult, badgeResult);
        var source = string.Join("+", new[] { nameResult.Source, previewResult.Source, timeResult.Source, badgeResult.Source }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal));

        return new UnreadConversationTextInfo(
            name,
            preview,
            time,
            unreadCount,
            confidence,
            string.IsNullOrWhiteSpace(source) ? "RowOcr" : source,
            string.Join(Environment.NewLine, rawParts));
    }

    public static UnreadConversationTextInfo BuildInfoFromRow(OcrResult rowResult)
    {
        var lines = Lines(rowResult.Text);
        var time = NormalizeTimeText(rowResult.Text);
        var unreadCount = FindUnreadCount(lines);
        var contentLines = lines
            .Where(line => !ContainsTimeText(line))
            .Where(line => FindUnreadCount([line]).Length == 0)
            .ToArray();
        var name = contentLines.FirstOrDefault() ?? string.Empty;
        var preview = contentLines.Skip(1).FirstOrDefault() ?? string.Empty;

        return new UnreadConversationTextInfo(
            name,
            preview,
            time,
            unreadCount,
            rowResult.Confidence,
            string.IsNullOrWhiteSpace(rowResult.Source) ? "RowOcr" : rowResult.Source,
            rowResult.Text.Trim());
    }

    public static string NormalizeUnreadCount(string? text)
    {
        var line = FirstLine(text);
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        var normalized = line
            .Replace('O', '0')
            .Replace('o', '0')
            .Replace('Ｉ', '1')
            .Replace('I', '1')
            .Replace('l', '1')
            .Replace('丨', '1');
        var match = NumberRegex.Match(normalized);
        return match.Success ? match.Value : string.Empty;
    }

    public static string NormalizeTimeText(string? text)
    {
        foreach (var line in Lines(text))
        {
            var normalized = line.Replace('：', ':');
            var match = TimeRegex.Match(normalized);
            if (match.Success)
            {
                return match.Value;
            }
        }

        return string.Empty;
    }

    public static string FirstLine(string? text)
    {
        return Lines(text).FirstOrDefault() ?? string.Empty;
    }

    private static IReadOnlyList<string> Lines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text.Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => WhitespaceRegex.Replace(line, " ").Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }

    private static string FindUnreadCount(IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            if (ContainsTimeText(line))
            {
                continue;
            }

            var normalized = NormalizeUnreadCount(line);
            if (!string.IsNullOrWhiteSpace(normalized) && line.Trim().Length <= 4)
            {
                return normalized;
            }
        }

        return string.Empty;
    }

    private static bool ContainsTimeText(string line)
    {
        return TimeRegex.IsMatch(line.Replace('：', ':'));
    }

    private static decimal AverageConfidence(params OcrResult[] results)
    {
        var values = results
            .Select(result => result.Confidence)
            .Where(confidence => confidence > 0)
            .ToArray();
        return values.Length == 0 ? 0m : values.Average();
    }
}
