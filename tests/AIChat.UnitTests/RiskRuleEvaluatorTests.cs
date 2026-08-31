using AIChat.Application.Risk;
using AIChat.Domain.Enums;

namespace AIChat.UnitTests;

public sealed class RiskRuleEvaluatorTests
{
    private readonly RiskRuleEvaluator _evaluator = new();

    [Fact]
    public void Evaluate_ShouldReturnHighRiskMatch_WhenKeywordMatches()
    {
        var ruleId = Guid.NewGuid();
        var matches = _evaluator.Evaluate(
            "这个订单我要投诉并要求赔偿。",
            [
                new RiskRuleCandidate(ruleId, "投诉赔偿", "投诉 赔偿", RiskLevel.High, RiskRuleAction.ManualReview),
                new RiskRuleCandidate(Guid.NewGuid(), "普通物流", "物流 快递", RiskLevel.Low, RiskRuleAction.MarkRisk)
            ]);

        Assert.Single(matches);
        Assert.Equal(ruleId, matches[0].RuleId);
        Assert.Equal(RiskLevel.High, _evaluator.GetHighestRiskLevel(matches));
        Assert.Equal(RiskRuleAction.ManualReview, matches[0].Action);
    }

    [Fact]
    public void GetHighestRiskLevel_ShouldReturnLow_WhenNoRuleMatches()
    {
        var matches = _evaluator.Evaluate(
            "这款还有黑色吗？",
            [new RiskRuleCandidate(Guid.NewGuid(), "投诉赔偿", "投诉 赔偿", RiskLevel.High, RiskRuleAction.ManualReview)]);

        Assert.Empty(matches);
        Assert.Equal(RiskLevel.Low, _evaluator.GetHighestRiskLevel(matches));
    }
}
