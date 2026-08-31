using AIChat.Application.AccessControl;
using AIChat.Domain.Entities;
using AIChat.Domain.Enums;

namespace AIChat.UnitTests;

public sealed class ClientAccessEvaluatorTests
{
    private readonly ClientAccessEvaluator _evaluator = new();

    [Fact]
    public void Evaluate_ShouldAllow_WhenPolicyIsEnabledAndWithinValidPeriod()
    {
        var now = DateTimeOffset.Parse("2026-07-29T08:00:00Z");
        var decision = _evaluator.Evaluate(
            CreateActiveEmployee(),
            new EmployeeClientAccessPolicy
            {
                Status = ClientAccessStatus.Enabled,
                ValidFromUtc = now.AddHours(-1),
                ValidToUtc = now.AddHours(1)
            },
            now);

        Assert.True(decision.CanStartTask);
        Assert.True(decision.CanContinueRun);
        Assert.Equal("Enabled", decision.Status);
    }

    [Fact]
    public void Evaluate_ShouldDeny_WhenPolicyIsExpired()
    {
        var now = DateTimeOffset.Parse("2026-07-29T08:00:00Z");
        var decision = _evaluator.Evaluate(
            CreateActiveEmployee(),
            new EmployeeClientAccessPolicy
            {
                Status = ClientAccessStatus.Enabled,
                ValidToUtc = now.AddSeconds(-1)
            },
            now);

        Assert.False(decision.CanStartTask);
        Assert.False(decision.CanContinueRun);
        Assert.Equal("Expired", decision.Status);
    }

    [Fact]
    public void Evaluate_ShouldDeny_WhenPolicyIsPaused()
    {
        var decision = _evaluator.Evaluate(
            CreateActiveEmployee(),
            new EmployeeClientAccessPolicy
            {
                Status = ClientAccessStatus.Paused,
                PauseReason = "员工离岗"
            },
            DateTimeOffset.UtcNow);

        Assert.False(decision.CanStartTask);
        Assert.Equal("Paused", decision.Status);
        Assert.Contains("员工离岗", decision.Reason);
    }

    [Fact]
    public void Evaluate_ShouldDeny_WhenSessionLimitIsReached()
    {
        var now = DateTimeOffset.Parse("2026-07-29T08:00:00Z");
        var decision = _evaluator.Evaluate(
            CreateActiveEmployee(),
            new EmployeeClientAccessPolicy
            {
                Status = ClientAccessStatus.Enabled,
                MaxSessionMinutes = 30
            },
            now,
            currentSessionStartedAtUtc: now.AddMinutes(-31));

        Assert.False(decision.CanContinueRun);
        Assert.Equal("SessionLimitReached", decision.Status);
    }

    private static Employee CreateActiveEmployee()
    {
        return new Employee
        {
            EmployeeNo = "E001",
            Name = "测试员工",
            IsActive = true
        };
    }
}
