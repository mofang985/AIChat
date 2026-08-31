using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIChat.RpaClient.Backend;

public sealed class RpaBackendClient(HttpClient httpClient)
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private string _apiBaseUrl = "https://localhost:7001";

    public void ConfigureBaseUrl(string apiBaseUrl)
    {
        _apiBaseUrl = apiBaseUrl.Trim().TrimEnd('/');
    }

    public async Task<AgentRegistrationResponse> RegisterAsync(RegisterAgentRequest request, CancellationToken cancellationToken)
    {
        return await PostAsync<RegisterAgentRequest, AgentRegistrationResponse>("api/agent/register", request, cancellationToken);
    }

    public async Task<AgentAccessPolicyResponse> SendHeartbeatAsync(AgentHeartbeatRequest request, CancellationToken cancellationToken)
    {
        return await PostAsync<AgentHeartbeatRequest, AgentAccessPolicyResponse>("api/agent/heartbeat", request, cancellationToken);
    }

    public async Task<RpaTaskDto> CreateTaskAsync(CreateRpaTaskRequest request, CancellationToken cancellationToken)
    {
        return await PostAsync<CreateRpaTaskRequest, RpaTaskDto>("api/agent/tasks", request, cancellationToken);
    }

    public async Task<RpaTaskDto> UpdateTaskStatusAsync(Guid taskId, string status, string? errorMessage, CancellationToken cancellationToken)
    {
        return await PutAsync<UpdateRpaTaskStatusRequest, RpaTaskDto>(
            $"api/agent/tasks/{taskId}/status",
            new UpdateRpaTaskStatusRequest(status, errorMessage),
            cancellationToken);
    }

    public async Task<RpaTaskDto> UpdateTaskResultAsync(Guid taskId, UpdateRpaTaskResultRequest request, CancellationToken cancellationToken)
    {
        return await PutAsync<UpdateRpaTaskResultRequest, RpaTaskDto>($"api/agent/tasks/{taskId}/result", request, cancellationToken);
    }

    public async Task<RpaActionLogDto> AddActionLogAsync(Guid taskId, CreateRpaActionLogRequest request, CancellationToken cancellationToken)
    {
        return await PostAsync<CreateRpaActionLogRequest, RpaActionLogDto>($"api/agent/tasks/{taskId}/action-logs", request, cancellationToken);
    }

    public async Task<IReadOnlyList<RpaTaskDto>> GetRecentRpaTasksAsync(CancellationToken cancellationToken)
    {
        return await GetAsync<List<RpaTaskDto>>("api/rpa-tasks", cancellationToken);
    }

    public async Task<ReplySuggestionDto> CreateReplySuggestionAsync(CreateReplySuggestionRequest request, CancellationToken cancellationToken)
    {
        return await PostAsync<CreateReplySuggestionRequest, ReplySuggestionDto>("api/ai/reply-suggestions", request, cancellationToken);
    }

    private async Task<TResponse> GetAsync<TResponse>(string relativePath, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(CreateApiUri(relativePath), cancellationToken);
        return await ReadResponseAsync<TResponse>(response, cancellationToken);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string relativePath, TRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(CreateApiUri(relativePath), request, _jsonOptions, cancellationToken);
        return await ReadResponseAsync<TResponse>(response, cancellationToken);
    }

    private async Task<TResponse> PutAsync<TRequest, TResponse>(string relativePath, TRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync(CreateApiUri(relativePath), request, _jsonOptions, cancellationToken);
        return await ReadResponseAsync<TResponse>(response, cancellationToken);
    }

    private async Task<TResponse> ReadResponseAsync<TResponse>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new RpaBackendException($"HTTP {(int)response.StatusCode}: {body}");
        }

        var result = await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions, cancellationToken);
        return result ?? throw new RpaBackendException("后端返回为空。");
    }

    private Uri CreateApiUri(string relativePath)
    {
        return new Uri($"{_apiBaseUrl}/{relativePath.TrimStart('/')}");
    }
}
