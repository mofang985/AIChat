using AIChat.Domain.Common;
using AIChat.Domain.Enums;

namespace AIChat.Domain.Entities;

public sealed class KnowledgeChunk : Entity
{
    public Guid? KnowledgeDocumentId { get; set; }
    public KnowledgeSourceType SourceType { get; set; } = KnowledgeSourceType.KnowledgeChunk;
    public Guid? SourceEntityId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Keywords { get; set; }
    public int ChunkIndex { get; set; }
    public string? VectorRef { get; set; }
    public string? EmbeddingModel { get; set; }
    public bool IsActive { get; set; } = true;

    public KnowledgeDocument? KnowledgeDocument { get; set; }
}
