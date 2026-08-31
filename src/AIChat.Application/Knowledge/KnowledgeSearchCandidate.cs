using AIChat.Domain.Enums;

namespace AIChat.Application.Knowledge;

public sealed record KnowledgeSearchCandidate(
    KnowledgeSourceType SourceType,
    Guid SourceId,
    string Title,
    string Content,
    string? Keywords,
    int Priority = 100);
