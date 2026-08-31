using AIChat.Domain.Common;
using AIChat.Domain.Enums;

namespace AIChat.Domain.Entities;

public sealed class VirtualDevice : Entity
{
    public Guid DeviceHostId { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? WeChatWorkAccountId { get; set; }
    public string VmName { get; set; } = string.Empty;
    public string? MachineCode { get; set; }
    public string? IpAddress { get; set; }
    public VirtualDeviceStatus Status { get; set; } = VirtualDeviceStatus.Active;
    public DateTimeOffset? LastSeenAtUtc { get; set; }
    public string? Notes { get; set; }

    public DeviceHost? DeviceHost { get; set; }
    public Employee? Employee { get; set; }
    public WeChatWorkAccount? WeChatWorkAccount { get; set; }
    public List<RpaClientInstance> RpaClientInstances { get; set; } = [];
}
