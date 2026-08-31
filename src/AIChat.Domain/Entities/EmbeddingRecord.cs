using AIChat.Domain.Common;
using AIChat.Domain.Enums;

namespace AIChat.Domain.Entities;

public sealed class EmbeddingRecord : Entity
{
    public KnowledgeSourceType SourceType { get; set; }
    public Guid SourceEntityId { get; set; }
    public string? ProviderCode { get; set; }
    public string EmbeddingModel { get; set; } = string.Empty;
    public string VectorRef { get; set; } = string.Empty;
    public string VectorVersion { get; set; } = "v1";
    public bool IsActive { get; set; } = true;
}
