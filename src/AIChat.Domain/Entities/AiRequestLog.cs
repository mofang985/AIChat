using AIChat.Domain.Common;
using AIChat.Domain.Enums;

namespace AIChat.Domain.Entities;

public sealed class AiRequestLog : Entity
{
    public string RequestType { get; set; } = string.Empty;
    public string? ProviderCode { get; set; }
    public string? ModelName { get; set; }
    public string? PromptTemplateCode { get; set; }
    public string? InputSummary { get; set; }
    public string? OutputSummary { get; set; }
    public AiRequestStatus Status { get; set; } = AiRequestStatus.Succeeded;
    public string? ErrorMessage { get; set; }
    public int? DurationMs { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
}
