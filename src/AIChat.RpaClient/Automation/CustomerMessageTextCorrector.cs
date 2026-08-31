namespace AIChat.RpaClient.Automation;

public static class CustomerMessageTextCorrector
{
    private static readonly (string Wrong, string Correct)[] ExactPhraseReplacements =
    [
        ("我首了", "我知道了"),
        ("我知首了", "我知道了"),
        ("1子", "你好"),
        ("丨子", "你好"),
        ("个子", "你好")
    ];

    public static string Correct(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var corrected = text.Trim();
        var compact = new string(corrected.Where(value => !char.IsWhiteSpace(value)).ToArray());
        foreach (var (wrong, correct) in ExactPhraseReplacements)
        {
            if (string.Equals(compact, wrong, StringComparison.Ordinal))
            {
                return correct;
            }
        }

        foreach (var (wrong, correct) in ExactPhraseReplacements)
        {
            corrected = corrected.Replace(wrong, correct, StringComparison.Ordinal);
        }

        return CorrectQuestionMarkOcr(corrected);
    }

    private static string CorrectQuestionMarkOcr(string value)
    {
        var trimmed = value.TrimEnd();
        if (trimmed.Length < 2 || trimmed[^1] != '7')
        {
            return value;
        }

        var beforeLast = trimmed[..^1].TrimEnd();
        if (beforeLast.Length == 0 || !EndsWithLikelyQuestionText(beforeLast))
        {
            return value;
        }

        return $"{beforeLast}？";
    }

    private static bool EndsWithLikelyQuestionText(string value)
    {
        return value.EndsWith("什么", StringComparison.Ordinal) ||
            value.EndsWith("吗", StringComparison.Ordinal) ||
            value.EndsWith("呢", StringComparison.Ordinal) ||
            value.EndsWith("谁", StringComparison.Ordinal) ||
            value.EndsWith("哪", StringComparison.Ordinal) ||
            value.EndsWith("多少", StringComparison.Ordinal) ||
            value.EndsWith("怎么", StringComparison.Ordinal);
    }
}
