using AIChat.Domain.Common;
using AIChat.Domain.Enums;

namespace AIChat.Domain.Entities;

public sealed class EmployeeClientAccessPolicy : Entity
{
    public Guid EmployeeId { get; set; }
    public ClientAccessStatus Status { get; set; } = ClientAccessStatus.Disabled;
    public DateTimeOffset? ValidFromUtc { get; set; }
    public DateTimeOffset? ValidToUtc { get; set; }
    public int? MaxDailyUsageMinutes { get; set; }
    public int? MaxSessionMinutes { get; set; }
    public string? PauseReason { get; set; }

    public Employee? Employee { get; set; }
}
