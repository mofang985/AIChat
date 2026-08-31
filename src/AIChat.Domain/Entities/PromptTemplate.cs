using AIChat.Domain.Common;
using AIChat.Domain.Enums;

namespace AIChat.Domain.Entities;

public sealed class PromptTemplate : Entity
{
    public string TemplateCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public PromptTemplateType TemplateType { get; set; } = PromptTemplateType.ReplySuggestion;
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPromptTemplate { get; set; } = string.Empty;
    public string Version { get; set; } = "v1";
    public bool IsActive { get; set; } = true;
}
