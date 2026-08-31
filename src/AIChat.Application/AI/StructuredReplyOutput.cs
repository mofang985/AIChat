using AIChat.Domain.Enums;

namespace AIChat.Application.AI;

public sealed record StructuredReplyOutput(
    string Intent,
    decimal Confidence,
    RiskLevel RiskLevel,
    string ReplyText,
    IReadOnlyList<string> KnowledgeRefs,
    bool ShouldAutoSend);
