using AIChat.Domain.Common;
using AIChat.Domain.Enums;

namespace AIChat.Domain.Entities;

public sealed class ReplySuggestion : Entity
{
    public Guid? RpaTaskId { get; set; }
    public string CustomerQuestion { get; set; } = string.Empty;
    public string? Intent { get; set; }
    public decimal Confidence { get; set; }
    public RiskLevel RiskLevel { get; set; } = RiskLevel.Low;
    public string ReplyText { get; set; } = string.Empty;
    public string KnowledgeRefsJson { get; set; } = "[]";
    public bool ShouldAutoSend { get; set; }
    public ReplySuggestionStatus Status { get; set; } = ReplySuggestionStatus.Generated;
    public string? FailureReason { get; set; }
    public string? ProviderCode { get; set; }
    public string? ModelName { get; set; }
    public string? RawAiResponse { get; set; }

    public RpaTask? RpaTask { get; set; }
}
