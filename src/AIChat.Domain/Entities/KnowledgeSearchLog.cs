using AIChat.Domain.Common;
using AIChat.Domain.Enums;

namespace AIChat.Domain.Entities;

public sealed class KnowledgeSearchLog : Entity
{
    public string Query { get; set; } = string.Empty;
    public KnowledgeSearchMode SearchMode { get; set; } = KnowledgeSearchMode.Keyword;
    public int HitCount { get; set; }
    public string ResultRefsJson { get; set; } = "[]";
}
