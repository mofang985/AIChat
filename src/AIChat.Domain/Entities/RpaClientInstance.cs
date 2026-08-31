using AIChat.Domain.Common;
using AIChat.Domain.Enums;

namespace AIChat.Domain.Entities;

public sealed class RpaClientInstance : Entity
{
    public Guid? VirtualDeviceId { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? WeChatWorkAccountId { get; set; }
    public string ClientInstanceKey { get; set; } = string.Empty;
    public string? ClientVersion { get; set; }
    public string? MachineName { get; set; }
    public RpaClientStatus Status { get; set; } = RpaClientStatus.Registered;
    public DateTimeOffset RegisteredAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastHeartbeatAtUtc { get; set; }
    public DateTimeOffset? CurrentSessionStartedAtUtc { get; set; }
    public bool LastCanContinueRun { get; set; }
    public string? LastAccessStatus { get; set; }
    public string? LastAccessReason { get; set; }

    public VirtualDevice? VirtualDevice { get; set; }
    public Employee? Employee { get; set; }
    public WeChatWorkAccount? WeChatWorkAccount { get; set; }
    public List<RpaTask> RpaTasks { get; set; } = [];
}
