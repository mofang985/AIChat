using System.Text.RegularExpressions;
using AIChat.RpaClient.Configuration;

namespace AIChat.RpaClient.Automation;

public sealed record VisionOcrReviewResult(
    bool Succeeded,
    string Text,
    ChatMessageSenderType SenderType,
    decimal Confidence,
    string Source,
    string? Reason,
    string? ErrorMessage)
{
    public static VisionOcrReviewResult Failed(string source, string errorMessage)
    {
        return new VisionOcrReviewResult(false, string.Empty, ChatMessageSenderType.Unknown, 0m, source, null, errorMessage);
    }
}

public sealed record VisionOcrMergeResult(
    bool IsUsable,
    OcrResult OcrResult,
    ChatMessageSenderType SenderType,
    bool UsedVisionReview,
    string? FailureReason);

public static class VisionOcrReviewPolicy
{
    public static bool ShouldReview(
        OcrResult ocrResult,
        ChatMessageSenderType senderType,
        RpaAutomationOptions options)
    {
        return ShouldReview(
            ocrResult,
            senderType,
            options.EnableVisionOcrReview,
            options.VisionReviewMode,
            options.OcrMinConfidence);
    }

    public static bool ShouldReview(
        OcrResult ocrResult,
        ChatMessageSenderType senderType,
        bool enabled,
        string? reviewMode,
        decimal minOcrConfidence)
    {
        if (!enabled)
        {
            return false;
        }

        if (senderType == ChatMessageSenderType.Unknown)
        {
            return true;
        }

        if (IsAlwaysForCustomerMessages(reviewMode) && senderType == ChatMessageSenderType.Customer)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(ocrResult.Text))
        {
            return senderType is ChatMessageSenderType.Customer or ChatMessageSenderType.Unknown;
        }

        if (ocrResult.Confidence < minOcrConfidence)
        {
            return senderType is ChatMessageSenderType.Customer or ChatMessageSenderType.Unknown;
        }

        return LooksSuspicious(ocrResult.Text);
    }

    public static bool ShouldSkipWhenVisionFails(RpaAutomationOptions options)
    {
        return !string.Equals(options.VisionOcrFailureBehavior, "UseOcrFallback", StringComparison.OrdinalIgnoreCase);
    }

    public static bool LooksSuspicious(string? text)
    {
        var rawText = text ?? string.Empty;
        var normalized = CustomerMessageExtractor.NormalizeForComparison(text ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        var cjkCount = normalized.Count(IsCjk);
        var digitCount = normalized.Count(char.IsDigit);
        if (cjkCount > 0 && digitCount > 0 && normalized.Length <= 6)
        {
            return true;
        }

        if (cjkCount > 0 && normalized.EndsWith("7", StringComparison.Ordinal) && normalized.Length <= 16)
        {
            return true;
        }

        return HasSuspiciousSymbolMix(rawText, cjkCount) ||
            Regex.IsMatch(rawText, @"[|`~\\]{2,}", RegexOptions.CultureInvariant);
    }

    private static bool HasSuspiciousSymbolMix(string text, int cjkCount)
    {
        if (cjkCount < 2)
        {
            return false;
        }

        return Regex.IsMatch(text, @"[/\\|`~_]", RegexOptions.CultureInvariant) ||
            Regex.IsMatch(text, @"[—–]+", RegexOptions.CultureInvariant);
    }

    private static bool IsAlwaysForCustomerMessages(string? reviewMode)
    {
        return string.Equals(reviewMode, "AlwaysForCustomerMessages", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCjk(char value)
    {
        return value is >= '\u4e00' and <= '\u9fff';
    }
}

public static class VisionOcrMergePolicy
{
    public static VisionOcrMergeResult Merge(
        OcrResult ocrResult,
        ChatMessageSenderType candidateSenderType,
        VisionOcrReviewResult? visionResult,
        decimal minVisionConfidence,
        bool skipWhenVisionFails)
    {
        if (visionResult is not null &&
            visionResult.Succeeded &&
            visionResult.Confidence >= minVisionConfidence)
        {
            var text = CleanVisionText(visionResult.Text);
            var senderType = visionResult.SenderType == ChatMessageSenderType.Unknown
                ? candidateSenderType
                : visionResult.SenderType;

            if (senderType == ChatMessageSenderType.Unknown)
            {
                return new VisionOcrMergeResult(
                    false,
                    ocrResult,
                    candidateSenderType,
                    true,
                    "Vision OCR could not confirm message sender.");
            }

            if (string.IsNullOrWhiteSpace(text) && senderType != ChatMessageSenderType.System)
            {
                return new VisionOcrMergeResult(
                    false,
                    ocrResult,
                    candidateSenderType,
                    true,
                    "Vision OCR returned empty text.");
            }

            var mergedText = string.IsNullOrWhiteSpace(text) ? ocrResult.Text.Trim() : text;
            if (string.IsNullOrWhiteSpace(mergedText) && senderType == ChatMessageSenderType.System)
            {
                mergedText = "system";
            }

            return new VisionOcrMergeResult(
                true,
                new OcrResult(mergedText, visionResult.Confidence, $"VisionOcr:{visionResult.Source}"),
                senderType,
                true,
                null);
        }

        if (skipWhenVisionFails)
        {
            var reason = visionResult?.ErrorMessage ??
                visionResult?.Reason ??
                "Vision OCR review did not return a usable result.";
            return new VisionOcrMergeResult(false, ocrResult, candidateSenderType, visionResult is not null, reason);
        }

        var fallbackText = ocrResult.Text.Trim();
        if (string.IsNullOrWhiteSpace(fallbackText) || candidateSenderType == ChatMessageSenderType.Unknown)
        {
            return new VisionOcrMergeResult(
                false,
                ocrResult,
                candidateSenderType,
                visionResult is not null,
                "OCR fallback is not usable.");
        }

        return new VisionOcrMergeResult(true, ocrResult, candidateSenderType, visionResult is not null, null);
    }

    private static string CleanVisionText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return Regex.Replace(text.Trim(), @"\s+", " ", RegexOptions.CultureInvariant);
    }
}
