namespace AIChat.Application.AI;

public sealed record LlmChatRequest(
    string BaseUrl,
    string ApiKey,
    string ProviderCode,
    string ModelName,
    string SystemPrompt,
    string UserPrompt,
    int TimeoutSeconds);
