using AIChat.Domain.Common;
using AIChat.Domain.Enums;

namespace AIChat.Domain.Entities;

public sealed class DeviceHost : Entity
{
    public string HostName { get; set; } = string.Empty;
    public string? AssetCode { get; set; }
    public string? IpAddress { get; set; }
    public int? CpuCores { get; set; }
    public int? MemoryGb { get; set; }
    public DeviceHostStatus Status { get; set; } = DeviceHostStatus.Active;
    public string? Notes { get; set; }

    public List<VirtualDevice> VirtualDevices { get; set; } = [];
}
