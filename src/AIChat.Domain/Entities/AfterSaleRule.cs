using AIChat.Domain.Common;

namespace AIChat.Domain.Entities;

public sealed class AfterSaleRule : Entity
{
    public string RuleCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Scenario { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? Keywords { get; set; }
    public int Priority { get; set; } = 100;
    public bool IsActive { get; set; } = true;
}
