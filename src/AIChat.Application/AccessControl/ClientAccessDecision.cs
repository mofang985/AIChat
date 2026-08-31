namespace AIChat.Application.AccessControl;

public sealed record ClientAccessDecision(
    bool CanStartTask,
    bool CanContinueRun,
    string Status,
    string Reason,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidToUtc,
    int? MaxDailyUsageMinutes,
    int? MaxSessionMinutes,
    int UsedDailyMinutes,
    int? CurrentSessionMinutes);
