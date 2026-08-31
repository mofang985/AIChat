using System.Text.RegularExpressions;

namespace AIChat.Application.Knowledge;

public sealed class KeywordKnowledgeSearchService
{
    private static readonly Regex TokenSplitter = new(@"[\s,，。；;、|/\\\r\n\t]+", RegexOptions.Compiled);

    public IReadOnlyList<KnowledgeSearchResult> Search(
        string query,
        IEnumerable<KnowledgeSearchCandidate> candidates,
        int maxResults = 5)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var normalizedQuery = Normalize(query);
        var queryTokens = SplitTokens(query).ToArray();
        var scored = new List<(KnowledgeSearchCandidate Candidate, decimal Score)>();

        foreach (var candidate in candidates)
        {
            var score = ScoreCandidate(normalizedQuery, queryTokens, candidate);
            if (score > 0)
            {
                scored.Add((candidate, score));
            }
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Candidate.Priority)
            .Take(Math.Max(1, maxResults))
            .Select(x => new KnowledgeSearchResult(
                x.Candidate.SourceType,
                x.Candidate.SourceId,
                x.Candidate.Title,
                BuildSnippet(x.Candidate.Content),
                x.Score))
            .ToArray();
    }

    private static decimal ScoreCandidate(
        string normalizedQuery,
        IReadOnlyCollection<string> queryTokens,
        KnowledgeSearchCandidate candidate)
    {
        var text = Normalize($"{candidate.Title} {candidate.Content} {candidate.Keywords}");
        var score = 0m;

        if (text.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            score += 8;
        }

        foreach (var token in queryTokens)
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 3;
            }
        }

        foreach (var keyword in SplitTokens(candidate.Keywords))
        {
            if (normalizedQuery.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 5;
            }
        }

        return score;
    }

    private static string BuildSnippet(string content)
    {
        var normalized = string.Join(' ', content.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 180 ? normalized : $"{normalized[..180]}...";
    }

    private static IEnumerable<string> SplitTokens(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        foreach (var token in TokenSplitter.Split(value))
        {
            var normalized = Normalize(token);
            if (normalized.Length >= 2)
            {
                yield return normalized;
            }
        }
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
    }
}
