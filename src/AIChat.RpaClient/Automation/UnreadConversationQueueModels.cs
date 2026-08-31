using System.Drawing;

namespace AIChat.RpaClient.Automation;

public sealed record UnreadConversationQueueSnapshot(
    DateTimeOffset ScannedAtUtc,
    Rectangle Region,
    IReadOnlyList<UnreadConversationCandidate> Candidates,
    string Summary,
    string? DebugCapturePath)
{
    public static UnreadConversationQueueSnapshot Empty(DateTimeOffset scannedAtUtc, Rectangle region, string reason)
    {
        return new UnreadConversationQueueSnapshot(scannedAtUtc, region, [], reason, null);
    }
}

public sealed record UnreadConversationCandidate(
    int VisualOrder,
    Rectangle BadgeBounds,
    Rectangle RowBounds,
    string UnreadHint,
    decimal Confidence,
    string Source,
    UnreadConversationTextInfo? TextInfo = null,
    UnreadConversationReadOnlyPreflight? Preflight = null)
{
    public string ToDisplayText()
    {
        var preflightText = Preflight is null ? string.Empty : $" / 预演 {Preflight.ToDisplayText()}";
        if (TextInfo is { HasAnyText: true })
        {
            return $"#{VisualOrder + 1} {TextInfo.ToDisplayText()}{preflightText} / 角标置信度 {Confidence:P0} / OCR {TextInfo.Confidence:P0}";
        }

        return $"#{VisualOrder + 1} {UnreadHint}{preflightText} / 置信度 {Confidence:P0} / Badge={Format(BadgeBounds)}";
    }

    private static string Format(Rectangle rectangle)
    {
        return $"X={rectangle.X},Y={rectangle.Y},W={rectangle.Width},H={rectangle.Height}";
    }
}

public sealed record UnreadBadgeDetection(Rectangle BadgeBounds, decimal Confidence, string Source, bool ContainsNumberGlyph = true);

internal static class UnreadConversationListGeometry
{
    public static int EstimateSearchHeaderHeight(int listWidth)
    {
        return Math.Clamp(
            (int)Math.Round(listWidth * 0.25d, MidpointRounding.AwayFromZero),
            54,
            96);
    }

    public static int EstimateConversationRowHeight(int listWidth)
    {
        return Math.Clamp(
            (int)Math.Round(listWidth * 0.31d, MidpointRounding.AwayFromZero),
            72,
            110);
    }

    public static int EstimateRowTolerance(Rectangle region)
    {
        return Math.Clamp(EstimateConversationRowHeight(region.Width) / 4, 12, 26);
    }

    public static bool LooksLikeUnreadBadgeLocal(Rectangle badgeBounds, int imageWidth, int imageHeight)
    {
        if (imageWidth <= 1 || imageHeight <= 1 || badgeBounds.Width <= 0 || badgeBounds.Height <= 0)
        {
            return false;
        }

        if (badgeBounds.Left < 0 || badgeBounds.Top < 0 || badgeBounds.Right > imageWidth || badgeBounds.Bottom > imageHeight)
        {
            return false;
        }

        var rowHeight = EstimateConversationRowHeight(imageWidth);
        var headerHeight = EstimateSearchHeaderHeight(imageWidth);
        var centerX = badgeBounds.Left + badgeBounds.Width / 2d;
        var centerY = badgeBounds.Top + badgeBounds.Height / 2d;
        if (centerY <= headerHeight)
        {
            return false;
        }

        var minCenterY = headerHeight + Math.Max(4d, rowHeight * 0.08d);
        if (centerY < minCenterY)
        {
            return false;
        }

        var minCenterX = Math.Clamp(
            (int)Math.Round(imageWidth * 0.15d, MidpointRounding.AwayFromZero),
            24,
            96);
        var maxCenterX = Math.Clamp(
            (int)Math.Round(imageWidth * 0.34d, MidpointRounding.AwayFromZero),
            60,
            180);
        if (centerX < minCenterX || centerX > maxCenterX)
        {
            return false;
        }

        var minSize = Math.Max(7, (int)Math.Round(rowHeight * 0.10d, MidpointRounding.AwayFromZero));
        var maxHeight = Math.Min(32, Math.Max(20, (int)Math.Round(rowHeight * 0.38d, MidpointRounding.AwayFromZero)));
        var maxWidth = Math.Min(56, Math.Max(24, (int)Math.Round(rowHeight * 0.70d, MidpointRounding.AwayFromZero)));
        if (badgeBounds.Width < minSize || badgeBounds.Height < minSize || badgeBounds.Width > maxWidth || badgeBounds.Height > maxHeight)
        {
            return false;
        }

        var aspect = badgeBounds.Width / (double)badgeBounds.Height;
        return aspect is >= 0.65d and <= 2.80d;
    }

    public static Rectangle CreateRowBounds(Rectangle region, Rectangle badgeBounds)
    {
        if (region.IsEmpty || region.Width <= 0 || region.Height <= 0 || badgeBounds.IsEmpty)
        {
            return Rectangle.Empty;
        }

        var rowHeight = Math.Min(EstimateConversationRowHeight(region.Width), region.Height);
        var centerY = badgeBounds.Top + badgeBounds.Height / 2d;
        var top = (int)Math.Round(centerY - rowHeight * 0.32d, MidpointRounding.AwayFromZero);
        top = Math.Clamp(top, region.Top, Math.Max(region.Top, region.Bottom - rowHeight));
        return new Rectangle(region.Left, top, region.Width, rowHeight);
    }
}


public static class UnreadConversationQueueAnalyzer
{
    public static IReadOnlyList<UnreadConversationCandidate> BuildQueue(
        Rectangle conversationListRegion,
        IReadOnlyList<UnreadBadgeDetection> detections,
        int maxCandidates)
    {
        if (conversationListRegion.IsEmpty || detections.Count == 0 || maxCandidates <= 0)
        {
            return [];
        }

        var rowTolerance = UnreadConversationListGeometry.EstimateRowTolerance(conversationListRegion);
        var rows = new List<UnreadBadgeDetection>();
        foreach (var detection in detections
            .Where(item => item.Confidence > 0 && item.ContainsNumberGlyph)
            .OrderBy(item => item.BadgeBounds.Top)
            .ThenByDescending(item => item.Confidence))
        {
            var duplicateRowIndex = rows.FindIndex(existing => IsSameConversationRow(existing.BadgeBounds, detection.BadgeBounds, rowTolerance));
            if (duplicateRowIndex < 0)
            {
                rows.Add(detection);
                continue;
            }

            if (detection.Confidence > rows[duplicateRowIndex].Confidence ||
                detection.BadgeBounds.Width * detection.BadgeBounds.Height > rows[duplicateRowIndex].BadgeBounds.Width * rows[duplicateRowIndex].BadgeBounds.Height)
            {
                rows[duplicateRowIndex] = detection;
            }
        }

        return rows
            .OrderBy(item => item.BadgeBounds.Top)
            .Take(maxCandidates)
            .Select((item, index) => new UnreadConversationCandidate(
                index,
                item.BadgeBounds,
                CreateRowBounds(conversationListRegion, item.BadgeBounds),
                CreateUnreadHint(item.BadgeBounds),
                item.Confidence,
                item.Source))
            .ToArray();
    }

    private static bool IsSameConversationRow(Rectangle first, Rectangle second, int tolerance)
    {
        var firstCenterY = first.Top + first.Height / 2;
        var secondCenterY = second.Top + second.Height / 2;
        return Math.Abs(firstCenterY - secondCenterY) <= tolerance;
    }

    private static Rectangle CreateRowBounds(Rectangle region, Rectangle badgeBounds)
    {
        return UnreadConversationListGeometry.CreateRowBounds(region, badgeBounds);
    }

    private static string CreateUnreadHint(Rectangle badgeBounds)
    {
        if (badgeBounds.Width >= badgeBounds.Height * 1.85)
        {
            return "多位数字未读候选";
        }

        return "数字未读候选";
    }
}
