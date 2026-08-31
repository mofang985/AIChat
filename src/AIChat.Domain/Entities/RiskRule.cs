using AIChat.Domain.Common;
using AIChat.Domain.Enums;

namespace AIChat.Domain.Entities;

public sealed class RiskRule : Entity
{
    public string RuleName { get; set; } = string.Empty;
    public string Keywords { get; set; } = string.Empty;
    public RiskLevel RiskLevel { get; set; } = RiskLevel.High;
    public RiskRuleAction Action { get; set; } = RiskRuleAction.ManualReview;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
}
