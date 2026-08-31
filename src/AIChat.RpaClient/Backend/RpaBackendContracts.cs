namespace AIChat.RpaClient.Backend;

public sealed record RegisterAgentRequest(
    string ClientInstanceKey,
    Guid? VirtualDeviceId,
    Guid? EmployeeId,
    Guid? WeChatWorkAccountId,
    string ClientVersion,
    string MachineName);

public sealed record AgentHeartbeatRequest(
    Guid? ClientInstanceId,
    string ClientInstanceKey,
    bool IsTaskRunning,
    DateTimeOffset? SessionStartedAtUtc,
    string ClientVersion,
    string MachineName);

public sealed record AgentRegistrationResponse(
    Guid ClientInstanceId,
    string ClientInstanceKey,
    Guid? VirtualDeviceId,
    string? VirtualDeviceName,
    Guid? EmployeeId,
    string? EmployeeName,
    Guid? WeChatWorkAccountId,
    AgentAccessPolicyResponse AccessPolicy);

public sealed record AgentAccessPolicyResponse(
    Guid ClientInstanceId,
    string ClientInstanceKey,
    bool CanStartTask,
    bool CanContinueRun,
    string Status,
    string Reason,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidToUtc,
    int? MaxDailyUsageMinutes,
    int? MaxSessionMinutes,
    int UsedDailyMinutes,
    int? CurrentSessionMinutes,
    DateTimeOffset? LastHeartbeatAtUtc);

public sealed record CreateRpaTaskRequest(
    Guid RpaClientInstanceId,
    Guid? EmployeeId,
    Guid? WeChatWorkAccountId,
    string TaskType,
    int? Priority,
    string? ConversationKey,
    string? CustomerDisplayName,
    string? IncomingMessageText,
    string? AiReplyText,
    string? RiskResult,
    DateTimeOffset? ScheduledAtUtc);

public sealed record UpdateRpaTaskStatusRequest(string Status, string? ErrorMessage);

public sealed record UpdateRpaTaskResultRequest(
    string? ConversationKey,
    string? CustomerDisplayName,
    string? IncomingMessageText,
    string? AiReplyText,
    string? RiskResult);

public sealed record CreateRpaActionLogRequest(
    string? Level,
    string? ActionName,
    string? Message,
    string? OcrText,
    string? AiReplyText,
    string? RiskResult,
    string? SanitizedScreenshotPath);

public sealed record RpaTaskDto(
    Guid Id,
    Guid TenantId,
    Guid RpaClientInstanceId,
    Guid? EmployeeId,
    Guid? WeChatWorkAccountId,
    string TaskType,
    string Status,
    int Priority,
    string? ConversationKey,
    string? CustomerDisplayName,
    string? IncomingMessageText,
    string? AiReplyText,
    string? RiskResult,
    DateTimeOffset? ScheduledAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record CreateReplySuggestionRequest(
    Guid? TenantId,
    Guid? RpaTaskId,
    string CustomerQuestion,
    string? ConversationContext,
    string? ProviderCode,
    string? PromptTemplateCode,
    int? MaxKnowledgeResults);

public sealed record ReplySuggestionDto(
    Guid Id,
    Guid TenantId,
    Guid? RpaTaskId,
    string CustomerQuestion,
    string? Intent,
    decimal Confidence,
    string RiskLevel,
    string ReplyText,
    string KnowledgeRefsJson,
    bool ShouldAutoSend,
    string Status,
    string? FailureReason,
    string? ProviderCode,
    string? ModelName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record RpaActionLogDto(
    Guid Id,
    Guid TenantId,
    Guid? RpaTaskId,
    Guid RpaClientInstanceId,
    string Level,
    string ActionName,
    string? Message,
    string? OcrText,
    string? AiReplyText,
    string? RiskResult,
    string? SanitizedScreenshotPath,
    DateTimeOffset LoggedAtUtc,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed class RpaBackendException(string message) : Exception(message);
