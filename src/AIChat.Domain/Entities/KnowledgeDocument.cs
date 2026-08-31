using AIChat.Domain.Common;

namespace AIChat.Domain.Entities;

public sealed class KnowledgeDocument : Entity
{
    public string Title { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? SourceName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastIndexedAtUtc { get; set; }

    public List<KnowledgeChunk> Chunks { get; set; } = [];
}
