using AIChat.Domain.Common;
using AIChat.Domain.Enums;

namespace AIChat.Domain.Entities;

public sealed class RpaTask : Entity
{
    public Guid RpaClientInstanceId { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? WeChatWorkAccountId { get; set; }
    public RpaTaskType TaskType { get; set; } = RpaTaskType.ReplyMessage;
    public RpaTaskStatus Status { get; set; } = RpaTaskStatus.Pending;
    public int Priority { get; set; } = 100;
    public string? ConversationKey { get; set; }
    public string? CustomerDisplayName { get; set; }
    public string? IncomingMessageText { get; set; }
    public string? AiReplyText { get; set; }
    public string? RiskResult { get; set; }
    public DateTimeOffset? ScheduledAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }

    public RpaClientInstance? RpaClientInstance { get; set; }
    public Employee? Employee { get; set; }
    public WeChatWorkAccount? WeChatWorkAccount { get; set; }
    public List<RpaActionLog> ActionLogs { get; set; } = [];
}
