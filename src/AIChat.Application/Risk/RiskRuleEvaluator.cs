using System.Text.RegularExpressions;
using AIChat.Domain.Enums;

namespace AIChat.Application.Risk;

public sealed class RiskRuleEvaluator
{
    private static readonly Regex TokenSplitter = new(@"[\s,，。；;、|/\\\r\n\t]+", RegexOptions.Compiled);

    public IReadOnlyList<RiskRuleMatch> Evaluate(string text, IEnumerable<RiskRuleCandidate> rules)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var normalizedText = text.ToLowerInvariant();
        var matches = new List<RiskRuleMatch>();

        foreach (var rule in rules)
        {
            foreach (var keyword in SplitKeywords(rule.Keywords))
            {
                if (normalizedText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(new RiskRuleMatch(rule.Id, rule.RuleName, keyword, rule.RiskLevel, rule.Action));
                    break;
                }
            }
        }

        return matches
            .OrderByDescending(x => x.RiskLevel)
            .ToArray();
    }

    public RiskLevel GetHighestRiskLevel(IEnumerable<RiskRuleMatch> matches, RiskLevel defaultLevel = RiskLevel.Low)
    {
        return matches.Any()
            ? matches.Max(x => x.RiskLevel)
            : defaultLevel;
    }

    private static IEnumerable<string> SplitKeywords(string value)
    {
        foreach (var token in TokenSplitter.Split(value))
        {
            var normalized = token.Trim().ToLowerInvariant();
            if (normalized.Length >= 2)
            {
                yield return normalized;
            }
        }
    }
}
