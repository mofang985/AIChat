using AIChat.Domain.Common;

namespace AIChat.Domain.Entities;

public sealed class Product : Entity
{
    public string ProductCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Brand { get; set; }
    public string? PriceText { get; set; }
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? Keywords { get; set; }
    public bool IsActive { get; set; } = true;
}
