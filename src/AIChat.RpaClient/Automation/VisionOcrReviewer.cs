using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using AIChat.RpaClient.Configuration;

namespace AIChat.RpaClient.Automation;

public sealed class VisionOcrReviewer(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<VisionOcrReviewResult> ReviewAsync(
        byte[] pngBytes,
        OcrResult ocrResult,
        ChatMessageSenderType candidateSenderType,
        RpaAutomationOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.EnableVisionOcrReview)
        {
            return VisionOcrReviewResult.Failed("VisionOcr", "Vision OCR review is disabled.");
        }

        if (!string.Equals(options.VisionOcrProvider, "Ollama", StringComparison.OrdinalIgnoreCase))
        {
            return VisionOcrReviewResult.Failed(options.VisionOcrProvider, "Unsupported vision OCR provider.");
        }

        if (string.IsNullOrWhiteSpace(options.VisionOcrBaseUrl))
        {
            return VisionOcrReviewResult.Failed("Ollama", "Vision OCR base URL is empty.");
        }

        if (string.IsNullOrWhiteSpace(options.VisionOcrModel))
        {
            return VisionOcrReviewResult.Failed("Ollama", "Vision OCR model is empty.");
        }

        if (!Uri.TryCreate($"{options.VisionOcrBaseUrl.TrimEnd('/')}/api/chat", UriKind.Absolute, out var endpoint))
        {
            return VisionOcrReviewResult.Failed("Ollama", "Vision OCR base URL is invalid.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.VisionOcrTimeoutSeconds)));

        try
        {
            var request = new
            {
                model = options.VisionOcrModel,
                stream = false,
                format = "json",
                messages = new object[]
                {
                    new
                    {
                        role = "user",
                        content = BuildPrompt(ocrResult, candidateSenderType),
                        images = new[] { Convert.ToBase64String(pngBytes) }
                    }
                },
                options = new
                {
                    temperature = 0
                }
            };

            using var response = await httpClient.PostAsJsonAsync(endpoint, request, JsonOptions, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return VisionOcrReviewResult.Failed(
                    Source(options),
                    $"Ollama returned HTTP {(int)response.StatusCode}: {Preview(body)}");
            }

            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var contentElement))
            {
                return VisionOcrReviewResult.Failed(Source(options), "Ollama response did not contain message.content.");
            }

            var content = contentElement.GetString();
            return ParseModelContent(content, Source(options));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return VisionOcrReviewResult.Failed(Source(options), "Vision OCR review timed out.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return VisionOcrReviewResult.Failed(Source(options), $"Vision OCR review failed: {ex.Message}");
        }
    }

    private static string BuildPrompt(OcrResult ocrResult, ChatMessageSenderType candidateSenderType)
    {
        return $$"""
You are an OCR reviewer for a cropped WeChat Windows chat message image.
Read only the text visible in this image and identify who sent this bubble.

Return JSON only:
{
  "senderType": "Customer|Self|System|Unknown",
  "text": "visible text, empty for pure system/time if unreadable",
  "confidence": 0.0,
  "reason": "short reason"
}

Rules:
- Customer means a left-side light/white customer bubble.
- Self means a right-side green self bubble.
- System means centered time/system notice.
- Unknown means text or sender cannot be reliably determined.
- The crop may include blank chat background around one message bubble. Read the message bubble text, not the blank background.
- If the bubble contains multiple visible lines, return all visible lines in natural reading order.
- If a WeChat emoji/sticker icon appears inline with text, include a short placeholder in the text.
- Use known WeChat placeholders when obvious, for example "[机智]"; otherwise use "[表情]".
- Do not summarize, rewrite, translate, infer, shorten, or omit visible words.
- Preserve Chinese characters, numbers, punctuation, and visible spaces as much as possible.
- Do not invent missing words.
- Prefer the image over the embedded OCR when they disagree.

Embedded OCR text: {{ocrResult.Text}}
Embedded OCR confidence: {{ocrResult.Confidence:0.0000}}
Initial sender guess: {{candidateSenderType}}
""";
    }

    private static VisionOcrReviewResult ParseModelContent(string? content, string source)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return VisionOcrReviewResult.Failed(source, "Vision model returned empty content.");
        }

        var json = ExtractJson(content);
        if (string.IsNullOrWhiteSpace(json))
        {
            return VisionOcrReviewResult.Failed(source, $"Vision model returned non-JSON content: {Preview(content)}");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var senderType = ParseSenderType(ReadString(root, "senderType"));
            var text = ReadString(root, "text")?.Trim() ?? string.Empty;
            var confidence = ReadDecimal(root, "confidence") ?? 0m;
            var reason = ReadString(root, "reason");

            return new VisionOcrReviewResult(
                true,
                text,
                senderType,
                Math.Clamp(confidence, 0m, 1m),
                source,
                reason,
                null);
        }
        catch (JsonException ex)
        {
            return VisionOcrReviewResult.Failed(source, $"Vision model JSON parse failed: {ex.Message}");
        }
    }

    private static string? ExtractJson(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            trimmed = trimmed.Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("```", string.Empty, StringComparison.Ordinal)
                .Trim();
        }

        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            return trimmed;
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        return start >= 0 && end > start
            ? trimmed[start..(end + 1)]
            : null;
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static decimal? ReadDecimal(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetDecimal(out var value) => value,
            JsonValueKind.String when decimal.TryParse(property.GetString(), out var value) => value,
            _ => null
        };
    }

    private static ChatMessageSenderType ParseSenderType(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "customer" => ChatMessageSenderType.Customer,
            "self" => ChatMessageSenderType.Self,
            "system" => ChatMessageSenderType.System,
            _ => ChatMessageSenderType.Unknown
        };
    }

    private static string Source(RpaAutomationOptions options)
    {
        return $"Ollama:{options.VisionOcrModel}";
    }

    private static string Preview(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 160 ? trimmed : $"{trimmed[..160]}...";
    }
}
