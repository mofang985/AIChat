using AIChat.Domain.Common;
using AIChat.Domain.Enums;

namespace AIChat.Domain.Entities;

public sealed class RpaActionLog : Entity
{
    public Guid? RpaTaskId { get; set; }
    public Guid RpaClientInstanceId { get; set; }
    public RpaActionLogLevel Level { get; set; } = RpaActionLogLevel.Info;
    public string ActionName { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? OcrText { get; set; }
    public string? AiReplyText { get; set; }
    public string? RiskResult { get; set; }
    public string? SanitizedScreenshotPath { get; set; }
    public DateTimeOffset LoggedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public RpaTask? RpaTask { get; set; }
    public RpaClientInstance? RpaClientInstance { get; set; }
}
