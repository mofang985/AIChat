using AIChat.Domain.Enums;

namespace AIChat.Application.Risk;

public sealed record RiskRuleMatch(
    Guid RuleId,
    string RuleName,
    string Keyword,
    RiskLevel RiskLevel,
    RiskRuleAction Action);
