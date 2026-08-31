namespace AIChat.RpaClient.Automation;

public static class OcrResultSelector
{
    public static OcrResult ChooseBetter(OcrResult first, OcrResult second)
    {
        if (string.IsNullOrWhiteSpace(first.Text))
        {
            return second;
        }

        if (string.IsNullOrWhiteSpace(second.Text))
        {
            return first;
        }

        var firstScore = GetQualityScore(first);
        var secondScore = GetQualityScore(second);
        var firstNormalized = CustomerMessageExtractor.NormalizeForComparison(first.Text);
        var secondNormalized = CustomerMessageExtractor.NormalizeForComparison(second.Text);

        if (LooksLikeMoreComplete(secondNormalized, firstNormalized) && secondScore + 0.18d >= firstScore)
        {
            return second;
        }

        if (LooksLikeMoreComplete(firstNormalized, secondNormalized) && firstScore + 0.18d >= secondScore)
        {
            return first;
        }

        return secondScore > firstScore ? second : first;
    }

    public static double GetQualityScore(OcrResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Text))
        {
            return 0;
        }

        var normalized = CustomerMessageExtractor.NormalizeForComparison(result.Text);
        var cjkCount = normalized.Count(IsCjk);
        var suspiciousPenalty = CalculateSuspiciousPenalty(normalized, cjkCount);
        var sourceBonus = result.Source.Contains("FocusedCrop", StringComparison.OrdinalIgnoreCase) ? 0.02d : 0d;
        return (double)result.Confidence + Math.Min(0.2d, cjkCount * 0.01d) + sourceBonus - suspiciousPenalty;
    }

    private static double CalculateSuspiciousPenalty(string normalized, int cjkCount)
    {
        if (string.IsNullOrWhiteSpace(normalized) || cjkCount == 0)
        {
            return 0;
        }

        var digitCount = normalized.Count(char.IsDigit);
        if (digitCount == 0)
        {
            return 0;
        }

        if (normalized.Length <= 4)
        {
            return 0.24d;
        }

        var last = normalized[^1];
        if (last == '7' && LooksLikeQuestionBeforeTrailingSeven(normalized[..^1]))
        {
            return 0.18d;
        }

        return digitCount >= cjkCount ? 0.16d : 0.08d;
    }

    private static bool LooksLikeQuestionBeforeTrailingSeven(string value)
    {
        return value.EndsWith("什么", StringComparison.Ordinal) ||
            value.EndsWith("吗", StringComparison.Ordinal) ||
            value.EndsWith("呢", StringComparison.Ordinal) ||
            value.EndsWith("谁", StringComparison.Ordinal) ||
            value.EndsWith("多少", StringComparison.Ordinal);
    }

    private static bool LooksLikeMoreComplete(string candidate, string current)
    {
        if (string.IsNullOrWhiteSpace(candidate) ||
            string.IsNullOrWhiteSpace(current) ||
            candidate.Length <= current.Length)
        {
            return false;
        }

        var extraChars = candidate.Length - current.Length;
        if (extraChars > Math.Max(8, current.Length / 2))
        {
            return false;
        }

        if (candidate.Contains(current, StringComparison.Ordinal))
        {
            return true;
        }

        var longestCommon = LongestCommonSubsequenceLength(candidate, current);
        return current.Length >= 4 && longestCommon >= Math.Ceiling(current.Length * 0.85d);
    }

    private static int LongestCommonSubsequenceLength(string first, string second)
    {
        var previous = new int[second.Length + 1];
        var current = new int[second.Length + 1];

        foreach (var firstChar in first)
        {
            Array.Clear(current);
            for (var secondIndex = 0; secondIndex < second.Length; secondIndex++)
            {
                current[secondIndex + 1] = firstChar == second[secondIndex]
                    ? previous[secondIndex] + 1
                    : Math.Max(previous[secondIndex + 1], current[secondIndex]);
            }

            (previous, current) = (current, previous);
        }

        return previous[second.Length];
    }

    private static bool IsCjk(char value)
    {
        return value is >= '\u4e00' and <= '\u9fff';
    }
}
