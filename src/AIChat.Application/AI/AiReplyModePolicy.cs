namespace AIChat.Application.AI;

public sealed record AiReplyModeParseResult(AiReplyMode Mode, bool IsValid, string? RawValue);

public sealed record AiReplyAutoSendDecision(bool IsAllowed, string? FailureReason);

public static class AiReplyModePolicy
{
    private const int MaxNoKnowledgeFallbackLength = 30;
    private const int MaxLlmOnlyReplyLength = 120;

    private static readonly string[] BusinessFactKeywords =
    [
        "商品", "价格", "价钱", "多少钱", "多少", "报价", "费用", "收费", "优惠", "活动", "折扣", "满减",
        "库存", "现货", "有货", "没货", "缺货", "发货", "什么时候发", "多久发", "物流", "快递", "运费", "包邮",
        "到货", "几天到", "多久到", "什么时候到", "售后", "退货", "退款", "换货", "退换", "保修", "赔偿", "赔付", "投诉", "承诺",
        "参数", "规格", "尺寸", "型号", "材质", "质量", "品质", "保质期", "正品", "发票"
    ];

    private static readonly string[] CapabilityBoundaryQuestionKeywords =
    [
        "\u5b9e\u65f6", "\u5929\u6c14", "\u65b0\u95fb", "\u7f51\u9875", "\u5916\u90e8", "\u5b98\u7f51",
        "\u67e5\u8be2", "\u67e5\u4e00\u4e0b", "\u5e2e\u6211\u67e5", "\u770b\u4e00\u4e0b"
    ];

    private static readonly string[] CapabilityBoundaryReplyMarkers =
    [
        "\u4e0d\u80fd\u76f4\u63a5", "\u65e0\u6cd5\u76f4\u63a5", "\u4e0d\u80fd\u5e2e\u60a8\u67e5", "\u4e0d\u80fd\u5e2e\u4f60\u67e5",
        "\u53ef\u4ee5\u901a\u8fc7", "\u5efa\u8bae\u60a8", "\u5efa\u8bae\u4f60", "\u67e5\u770b"
    ];

    private static readonly string[] CapabilityBoundaryUnsafeReplyMarkers =
    [
        "\u6211\u5e2e\u60a8\u67e5", "\u6211\u5e2e\u4f60\u67e5", "\u6b63\u5728\u67e5\u8be2", "\u67e5\u8be2\u7ed3\u679c"
    ];

    public static AiReplyModeParseResult Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new AiReplyModeParseResult(AiReplyMode.KnowledgeFirst, true, value);
        }

        return Enum.TryParse<AiReplyMode>(value.Trim(), ignoreCase: true, out var mode) && Enum.IsDefined(mode)
            ? new AiReplyModeParseResult(mode, true, value)
            : new AiReplyModeParseResult(AiReplyMode.KnowledgeFirst, false, value);
    }

    public static bool IsSafeNoKnowledgeFallbackReply(string? replyText)
    {
        var compact = Compact(replyText);
        return compact.Length is >= 1 and <= MaxNoKnowledgeFallbackLength &&
            !ContainsBusinessFactKeyword(compact);
    }

    public static AiReplyAutoSendDecision EvaluateLlmOnlyAutoSend(
        string? customerQuestion,
        string? replyText,
        bool enableBusinessFactGuard = true)
    {
        var compactReply = Compact(replyText);
        if (compactReply.Length == 0)
        {
            return new AiReplyAutoSendDecision(false, "LlmOnly 模式下 AI 回复为空，需要人工复核。");
        }

        if (compactReply.Length > MaxLlmOnlyReplyLength)
        {
            return new AiReplyAutoSendDecision(false, "LlmOnly 模式下回复过长，需要人工复核。");
        }

        if (enableBusinessFactGuard &&
            (ContainsBusinessFactKeyword(customerQuestion) || ContainsBusinessFactKeyword(compactReply)))
        {
            return new AiReplyAutoSendDecision(false, "LlmOnly 模式下命中价格、库存、物流、售后等业务事实或承诺风险，需要人工复核。");
        }

        return new AiReplyAutoSendDecision(true, null);
    }

    public static bool CanOverrideModelAutoSendForSafeCapabilityBoundary(
        string? customerQuestion,
        string? replyText)
    {
        var compactQuestion = Compact(customerQuestion);
        var compactReply = Compact(replyText);
        if (compactQuestion.Length == 0 ||
            compactReply.Length == 0 ||
            compactReply.Length > MaxLlmOnlyReplyLength)
        {
            return false;
        }

        if (ContainsBusinessFactKeyword(compactQuestion) || ContainsBusinessFactKeyword(compactReply))
        {
            return false;
        }

        if (!CapabilityBoundaryQuestionKeywords.Any(keyword => compactQuestion.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (CapabilityBoundaryUnsafeReplyMarkers.Any(marker => compactReply.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return CapabilityBoundaryReplyMarkers.Any(marker => compactReply.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    public static bool ContainsBusinessFactKeyword(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return BusinessFactKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static string Compact(string? text)
    {
        return new string((text ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());
    }
}
