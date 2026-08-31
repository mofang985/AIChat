using AIChat.Domain.Common;
using AIChat.Domain.Enums;

namespace AIChat.Domain.Entities;

public sealed class LlmProviderConfig : Entity
{
    public string ProviderCode { get; set; } = string.Empty;
    public LlmProviderType ProviderType { get; set; } = LlmProviderType.OpenAICompatible;
    public string DisplayName { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string ApiKeyEnvironmentVariable { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 60;
    public string? Notes { get; set; }
}
