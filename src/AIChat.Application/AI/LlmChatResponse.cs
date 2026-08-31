namespace AIChat.Application.AI;

public sealed record LlmChatResponse(
    bool Succeeded,
    string? Content,
    string? ErrorMessage,
    int? DurationMs,
    int? PromptTokens,
    int? CompletionTokens);
