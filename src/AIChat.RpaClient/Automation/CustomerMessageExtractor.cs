using System.Security.Cryptography;
using System.Text;

namespace AIChat.RpaClient.Automation;

public static class CustomerMessageExtractor
{
    public static string ExtractLatest(string? ocrText)
    {
        return ExtractSnapshot(ocrText)?.LatestMessage ?? string.Empty;
    }

    public static CustomerMessageSnapshot? ExtractSnapshot(string? incomingOcrText, string? conversationContextText = null)
    {
        var incomingLines = SplitUsefulLines(incomingOcrText);
        var contextLines = SplitUsefulLines(conversationContextText);
        var incomingMessageCandidates = incomingLines
            .Where(line => !LooksLikeWeChatSystemNotice(line))
            .ToArray();
        var contextMessageCandidates = contextLines
            .Where(line => !LooksLikeWeChatSystemNotice(line))
            .ToArray();

        if (incomingMessageCandidates.Length == 0)
        {
            return null;
        }

        var latestMessage = incomingMessageCandidates.Last();
        var hasConversationTextAfterLatestMessage = HasConversationTextAfterLatestMessage(
            contextMessageCandidates,
            latestMessage);
        var conversationContextLines = contextMessageCandidates.Length > 0
            ? contextMessageCandidates.ToList()
            : incomingMessageCandidates.ToList();
        if (!conversationContextLines.Any(line => IsSameMessageLine(line, latestMessage)))
        {
            conversationContextLines.Add(latestMessage);
        }

        var conversationContext = string.Join(Environment.NewLine, conversationContextLines);
        var rawLineCount = incomingLines.Length;
        var fingerprint = CreateFingerprint(latestMessage, conversationContext, rawLineCount);

        return new CustomerMessageSnapshot(
            latestMessage,
            conversationContext,
            fingerprint,
            rawLineCount,
            hasConversationTextAfterLatestMessage);
    }

    public static string NormalizeForComparison(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value
            .Where(character => !char.IsWhiteSpace(character))
            .Where(IsComparableCharacter)
            .ToArray())
            .ToUpperInvariant();
    }

    public static bool AreSameMessage(string? first, string? second)
    {
        var normalizedFirst = NormalizeForComparison(first);
        var normalizedSecond = NormalizeForComparison(second);
        if (string.IsNullOrWhiteSpace(normalizedFirst) || string.IsNullOrWhiteSpace(normalizedSecond))
        {
            return false;
        }

        if (string.Equals(normalizedFirst, normalizedSecond, StringComparison.Ordinal))
        {
            return true;
        }

        var firstWithoutSpeaker = RemoveKnownSpeakerPrefix(normalizedFirst);
        var secondWithoutSpeaker = RemoveKnownSpeakerPrefix(normalizedSecond);
        if (string.Equals(firstWithoutSpeaker, secondWithoutSpeaker, StringComparison.Ordinal))
        {
            return true;
        }

        var shorter = firstWithoutSpeaker.Length <= secondWithoutSpeaker.Length
            ? firstWithoutSpeaker
            : secondWithoutSpeaker;
        var longer = firstWithoutSpeaker.Length > secondWithoutSpeaker.Length
            ? firstWithoutSpeaker
            : secondWithoutSpeaker;

        return shorter.Length >= 2 && longer.EndsWith(shorter, StringComparison.Ordinal);
    }

    public static bool ContainsComparableText(string? text, string? expected)
    {
        var normalizedText = NormalizeForComparison(text);
        var normalizedExpected = NormalizeForComparison(expected);
        return !string.IsNullOrWhiteSpace(normalizedText) &&
            !string.IsNullOrWhiteSpace(normalizedExpected) &&
            normalizedText.Contains(normalizedExpected, StringComparison.Ordinal);
    }

    public static bool IsWeChatSystemNotice(string? line)
    {
        return string.IsNullOrWhiteSpace(line) || LooksLikeWeChatSystemNotice(line);
    }

    private static string[] SplitUsefulLines(string? ocrText)
    {
        if (string.IsNullOrWhiteSpace(ocrText))
        {
            return [];
        }

        return ocrText
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }

    private static bool LooksLikeWeChatSystemNotice(string line)
    {
        var normalized = NormalizeForComparison(line);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        return normalized.StartsWith("你已添加了", StringComparison.Ordinal) ||
            normalized.Contains("现在可以开始聊天了", StringComparison.Ordinal) ||
            normalized.Contains("以上是打招呼的内容", StringComparison.Ordinal) ||
            normalized.Contains("通过了你的朋友验证请求", StringComparison.Ordinal) ||
            normalized.Contains("开启了朋友验证", StringComparison.Ordinal) ||
            normalized.Contains("你撤回了一条消息", StringComparison.Ordinal) ||
            normalized.Contains("对方撤回了一条消息", StringComparison.Ordinal);
    }

    private static bool HasConversationTextAfterLatestMessage(string[] contextLines, string latestMessage)
    {
        if (contextLines.Length == 0)
        {
            return false;
        }

        var normalizedLatest = NormalizeForComparison(latestMessage);
        if (string.IsNullOrWhiteSpace(normalizedLatest))
        {
            return false;
        }

        var latestIndex = -1;
        for (var index = 0; index < contextLines.Length; index++)
        {
            if (IsSameMessageLine(contextLines[index], latestMessage))
            {
                latestIndex = index;
            }
        }

        if (latestIndex < 0 || latestIndex >= contextLines.Length - 1)
        {
            return false;
        }

        return contextLines
            .Skip(latestIndex + 1)
            .Select(NormalizeForComparison)
            .Any(value => !string.IsNullOrWhiteSpace(value) && value != normalizedLatest);
    }

    private static bool IsSameMessageLine(string contextLine, string latestMessage)
    {
        return AreSameMessage(contextLine, latestMessage);
    }

    private static string RemoveKnownSpeakerPrefix(string normalizedValue)
    {
        foreach (var prefix in KnownSpeakerPrefixes)
        {
            if (normalizedValue.Length > prefix.Length &&
                normalizedValue.StartsWith(prefix, StringComparison.Ordinal))
            {
                return normalizedValue[prefix.Length..];
            }
        }

        return normalizedValue;
    }

    private static string CreateFingerprint(string latestMessage, string conversationContext, int rawLineCount)
    {
        var normalizedLatest = NormalizeForComparison(latestMessage);
        var normalizedContext = NormalizeForComparison(conversationContext);
        if (normalizedContext.Length > 800)
        {
            normalizedContext = normalizedContext[^800..];
        }

        var raw = $"{normalizedLatest}|{normalizedContext}|{rawLineCount}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..24].ToLowerInvariant();
    }

    private static bool IsComparableCharacter(char value)
    {
        return char.IsLetterOrDigit(value) || value is >= '\u4e00' and <= '\u9fff';
    }

    private static readonly string[] KnownSpeakerPrefixes =
    [
        "客户",
        "客服",
        "用户",
        "顾客",
        "买家",
        "卖家",
        "员工",
        "店员",
        "商家",
        "我方",
        "对方",
        "自己",
        "助手",
        "我"
    ];
}

public sealed record CustomerMessageSnapshot(
    string LatestMessage,
    string ConversationContext,
    string Fingerprint,
    int RawLineCount,
    bool HasConversationTextAfterLatestMessage)
{
    public bool HasMessage => !string.IsNullOrWhiteSpace(LatestMessage);
}
