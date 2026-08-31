using AIChat.Domain.Common;

namespace AIChat.Domain.Entities;

public sealed class FaqItem : Entity
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Keywords { get; set; }
    public int Priority { get; set; } = 100;
    public bool IsActive { get; set; } = true;
}
