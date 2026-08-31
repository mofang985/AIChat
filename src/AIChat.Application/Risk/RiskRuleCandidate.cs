using AIChat.Domain.Enums;

namespace AIChat.Application.Risk;

public sealed record RiskRuleCandidate(
    Guid Id,
    string RuleName,
    string Keywords,
    RiskLevel RiskLevel,
    RiskRuleAction Action);
