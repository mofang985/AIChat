using AIChat.Domain.Common;
using AIChat.Domain.Enums;

namespace AIChat.Domain.Entities;

public sealed class WeChatWorkAccount : Entity
{
    public Guid EmployeeId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string WeChatId { get; set; } = string.Empty;
    public string? PhoneNumberMasked { get; set; }
    public WeChatWorkAccountStatus Status { get; set; } = WeChatWorkAccountStatus.Active;
    public string? Notes { get; set; }

    public Employee? Employee { get; set; }
    public List<RpaClientInstance> RpaClientInstances { get; set; } = [];
    public List<RpaTask> RpaTasks { get; set; } = [];
}
