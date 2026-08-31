using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AIChat.Application.AI;

namespace AIChat.Infrastructure.AI;

public sealed class OpenAICompatibleLlmProvider(HttpClient httpClient) : ILlmProvider
{
    public async Task<LlmChatResponse> GenerateAsync(LlmChatRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BaseUrl))
        {
            return Failed("LLM provider BaseUrl is not configured.");
        }

        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return Failed("LLM provider API key is not configured.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, request.TimeoutSeconds)));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var endpoint = new Uri($"{request.BaseUrl.TrimEnd('/')}/chat/completions");
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey);
            httpRequest.Content = JsonContent.Create(new
            {
                model = request.ModelName,
                messages = new object[]
                {
                    new { role = "system", content = request.SystemPrompt },
                    new { role = "user", content = request.UserPrompt }
                },
                temperature = 0.2,
                response_format = new { type = "json_object" }
            });

            using var response = await httpClient.SendAsync(httpRequest, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                return Failed($"LLM provider returned HTTP {(int)response.StatusCode}.", (int)stopwatch.ElapsedMilliseconds);
            }

            using var document = JsonDocument.Parse(body);
            var content = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            var usage = document.RootElement.TryGetProperty("usage", out var usageElement)
                ? usageElement
                : default;

            return new LlmChatResponse(
                Succeeded: true,
                Content: content,
                ErrorMessage: null,
                DurationMs: (int)stopwatch.ElapsedMilliseconds,
                PromptTokens: ReadOptionalInt(usage, "prompt_tokens"),
                CompletionTokens: ReadOptionalInt(usage, "completion_tokens"));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return Failed("LLM provider call timed out.", (int)stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return Failed($"LLM provider call failed: {ex.Message}", (int)stopwatch.ElapsedMilliseconds);
        }
    }

    private static LlmChatResponse Failed(string errorMessage, int? durationMs = null)
    {
        return new LlmChatResponse(false, null, errorMessage, durationMs, null, null);
    }

    private static int? ReadOptionalInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Undefined ||
            !element.TryGetProperty(propertyName, out var property) ||
            !property.TryGetInt32(out var value))
        {
            return null;
        }

        return value;
    }
}
