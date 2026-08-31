using AIChat.Domain.Entities;
using AIChat.Domain.Enums;

namespace AIChat.Application.AccessControl;

public sealed class ClientAccessEvaluator
{
    public ClientAccessDecision Evaluate(
        Employee? employee,
        EmployeeClientAccessPolicy? policy,
        DateTimeOffset nowUtc,
        int usedDailyMinutes = 0,
        DateTimeOffset? currentSessionStartedAtUtc = null)
    {
        if (employee is null || !employee.IsActive)
        {
            return Denied("Disabled", "员工不存在或已停用。", policy, usedDailyMinutes, currentSessionStartedAtUtc, nowUtc);
        }

        if (policy is null)
        {
            return Denied("NotConfigured", "员工客户端授权尚未配置。", null, usedDailyMinutes, currentSessionStartedAtUtc, nowUtc);
        }

        if (policy.Status == ClientAccessStatus.Disabled)
        {
            return Denied("Disabled", "员工客户端授权已禁用。", policy, usedDailyMinutes, currentSessionStartedAtUtc, nowUtc);
        }

        if (policy.Status == ClientAccessStatus.Paused)
        {
            var reason = string.IsNullOrWhiteSpace(policy.PauseReason)
                ? "员工客户端授权已暂停。"
                : $"员工客户端授权已暂停：{policy.PauseReason}";

            return Denied("Paused", reason, policy, usedDailyMinutes, currentSessionStartedAtUtc, nowUtc);
        }

        if (policy.ValidFromUtc is not null && nowUtc < policy.ValidFromUtc.Value)
        {
            return Denied("NotStarted", "员工客户端授权尚未开始。", policy, usedDailyMinutes, currentSessionStartedAtUtc, nowUtc);
        }

        if (policy.ValidToUtc is not null && nowUtc > policy.ValidToUtc.Value)
        {
            return Denied("Expired", "员工客户端授权已过期。", policy, usedDailyMinutes, currentSessionStartedAtUtc, nowUtc);
        }

        if (policy.MaxDailyUsageMinutes is not null && usedDailyMinutes >= policy.MaxDailyUsageMinutes.Value)
        {
            return Denied("DailyLimitReached", "员工客户端今日使用时长已达到上限。", policy, usedDailyMinutes, currentSessionStartedAtUtc, nowUtc);
        }

        var sessionMinutes = GetCurrentSessionMinutes(currentSessionStartedAtUtc, nowUtc);
        if (policy.MaxSessionMinutes is not null &&
            sessionMinutes is not null &&
            sessionMinutes.Value >= policy.MaxSessionMinutes.Value)
        {
            return Denied("SessionLimitReached", "员工客户端单次会话使用时长已达到上限。", policy, usedDailyMinutes, currentSessionStartedAtUtc, nowUtc);
        }

        return new ClientAccessDecision(
            CanStartTask: true,
            CanContinueRun: true,
            Status: "Enabled",
            Reason: "员工客户端授权有效。",
            ValidFromUtc: policy.ValidFromUtc,
            ValidToUtc: policy.ValidToUtc,
            MaxDailyUsageMinutes: policy.MaxDailyUsageMinutes,
            MaxSessionMinutes: policy.MaxSessionMinutes,
            UsedDailyMinutes: usedDailyMinutes,
            CurrentSessionMinutes: sessionMinutes);
    }

    private static ClientAccessDecision Denied(
        string status,
        string reason,
        EmployeeClientAccessPolicy? policy,
        int usedDailyMinutes,
        DateTimeOffset? currentSessionStartedAtUtc,
        DateTimeOffset nowUtc)
    {
        return new ClientAccessDecision(
            CanStartTask: false,
            CanContinueRun: false,
            Status: status,
            Reason: reason,
            ValidFromUtc: policy?.ValidFromUtc,
            ValidToUtc: policy?.ValidToUtc,
            MaxDailyUsageMinutes: policy?.MaxDailyUsageMinutes,
            MaxSessionMinutes: policy?.MaxSessionMinutes,
            UsedDailyMinutes: usedDailyMinutes,
            CurrentSessionMinutes: GetCurrentSessionMinutes(currentSessionStartedAtUtc, nowUtc));
    }

    private static int? GetCurrentSessionMinutes(DateTimeOffset? currentSessionStartedAtUtc, DateTimeOffset nowUtc)
    {
        if (currentSessionStartedAtUtc is null)
        {
            return null;
        }

        var minutes = (int)Math.Floor((nowUtc - currentSessionStartedAtUtc.Value).TotalMinutes);
        return Math.Max(0, minutes);
    }
}
