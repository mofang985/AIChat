using System.Text.Json;
using System.Text.RegularExpressions;
using AIChat.Domain.Enums;

namespace AIChat.Application.AI;

public sealed class StructuredReplyParser
{
    private static readonly Regex JsonFenceRegex = new(@"```(?:json)?\s*(?<json>[\s\S]*?)\s*```", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public bool TryParseReply(string? rawContent, out StructuredReplyOutput output, out string errorMessage)
    {
        output = new StructuredReplyOutput(string.Empty, 0, RiskLevel.High, string.Empty, [], false);
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            errorMessage = "AI response is empty.";
            return false;
        }

        var json = ExtractJson(rawContent);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var intent = ReadString(root, "Intent", "intent");
            var replyText = ReadString(root, "ReplyText", "replyText", "reply_text");

            if (string.IsNullOrWhiteSpace(intent) || string.IsNullOrWhiteSpace(replyText))
            {
                errorMessage = "AI response must contain Intent and ReplyText.";
                return false;
            }

            var riskLevelText = ReadString(root, "RiskLevel", "riskLevel", "risk_level");
            if (!Enum.TryParse<RiskLevel>(riskLevelText, ignoreCase: true, out var riskLevel))
            {
                errorMessage = "AI response RiskLevel is invalid.";
                return false;
            }

            output = new StructuredReplyOutput(
                intent,
                ReadDecimal(root, "Confidence", "confidence"),
                riskLevel,
                replyText,
                ReadStringArray(root, "KnowledgeRefs", "knowledgeRefs", "knowledge_refs"),
                ReadBoolean(root, "ShouldAutoSend", "shouldAutoSend", "should_auto_send"));

            return true;
        }
        catch (JsonException ex)
        {
            errorMessage = $"AI response is not valid JSON: {ex.Message}";
            return false;
        }
    }

    private static string ExtractJson(string rawContent)
    {
        var match = JsonFenceRegex.Match(rawContent.Trim());
        return match.Success ? match.Groups["json"].Value : rawContent.Trim();
    }

    private static string ReadString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static decimal ReadDecimal(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
                {
                    return Math.Clamp(number, 0, 1);
                }

                if (value.ValueKind == JsonValueKind.String &&
                    decimal.TryParse(value.GetString(), out var parsed))
                {
                    return Math.Clamp(parsed, 0, 1);
                }
            }
        }

        return 0;
    }

    private static bool ReadBoolean(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value))
            {
                if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    return value.GetBoolean();
                }

                if (value.ValueKind == JsonValueKind.String &&
                    bool.TryParse(value.GetString(), out var parsed))
                {
                    return parsed;
                }
            }
        }

        return false;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            return value
                .EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .ToArray();
        }

        return [];
    }
}
