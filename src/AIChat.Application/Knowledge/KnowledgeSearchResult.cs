using AIChat.Domain.Enums;

namespace AIChat.Application.Knowledge;

public sealed record KnowledgeSearchResult(
    KnowledgeSourceType SourceType,
    Guid SourceId,
    string Title,
    string Snippet,
    decimal Score);
