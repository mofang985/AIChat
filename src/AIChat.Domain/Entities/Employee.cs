using AIChat.Domain.Common;

namespace AIChat.Domain.Entities;

public sealed class Employee : Entity
{
    public string EmployeeNo { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    public EmployeeClientAccessPolicy? ClientAccessPolicy { get; set; }
    public List<WeChatWorkAccount> WeChatWorkAccounts { get; set; } = [];
    public List<VirtualDevice> VirtualDevices { get; set; } = [];
    public List<RpaClientInstance> RpaClientInstances { get; set; } = [];
}
