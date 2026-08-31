namespace AIChat.Application.AI;

public interface ILlmProvider
{
    Task<LlmChatResponse> GenerateAsync(LlmChatRequest request, CancellationToken cancellationToken);
}
