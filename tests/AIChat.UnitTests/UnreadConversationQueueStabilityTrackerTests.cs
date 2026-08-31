using System.Drawing;
using AIChat.RpaClient.Automation;
using AIChat.RpaClient.Configuration;

namespace AIChat.UnitTests;

public sealed class UnreadConversationQueueStabilityTrackerTests
{
    [Fact]
    public void Apply_ShouldPromoteCandidateAfterRequiredStableScans()
    {
        var tracker = new UnreadConversationQueueStabilityTracker();
        var options = CreateOptions();
        var first = tracker.Apply(CreateSnapshot(DateTimeOffset.Parse("2026-08-13T10:00:00Z"), CreateCandidate()), options);
        var second = tracker.Apply(CreateSnapshot(DateTimeOffset.Parse("2026-08-13T10:00:06Z"), CreateCandidate(rowTop: 102)), options);

        var firstPreflight = Assert.Single(first.Candidates).Preflight;
        Assert.NotNull(firstPreflight);
        Assert.False(firstPreflight.IsStable);
        Assert.Equal("观察中", firstPreflight.StatusText);
        Assert.Equal(1, firstPreflight.StableScanCount);
        Assert.Contains("可切换候选 0 个", first.Summary, StringComparison.Ordinal);

        var secondPreflight = Assert.Single(second.Candidates).Preflight;
        Assert.NotNull(secondPreflight);
        Assert.True(secondPreflight.IsStable);
        Assert.Equal("可切换候选", secondPreflight.StatusText);
        Assert.Equal(2, secondPreflight.StableScanCount);
        Assert.Contains("可切换候选 1 个", second.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_ShouldResetStabilityWhenRowMovesTooFar()
    {
        var tracker = new UnreadConversationQueueStabilityTracker();
        var options = CreateOptions();
        _ = tracker.Apply(CreateSnapshot(DateTimeOffset.Parse("2026-08-13T10:00:00Z"), CreateCandidate(rowTop: 100)), options);
        var moved = tracker.Apply(CreateSnapshot(DateTimeOffset.Parse("2026-08-13T10:00:06Z"), CreateCandidate(rowTop: 150)), options);

        var preflight = Assert.Single(moved.Candidates).Preflight;
        Assert.NotNull(preflight);
        Assert.False(preflight.IsStable);
        Assert.Equal(1, preflight.StableScanCount);
        Assert.Contains("观察中 1 个", moved.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_ShouldAcceptCandidateWhenNameIsMissingButOtherTextExists()
    {
        var tracker = new UnreadConversationQueueStabilityTracker();
        var options = CreateOptions();
        var snapshot = tracker.Apply(
            CreateSnapshot(
                DateTimeOffset.Parse("2026-08-13T10:00:00Z"),
                CreateCandidate(textInfo: new UnreadConversationTextInfo(string.Empty, "摘要", "16:13", "1", 0.8m, "Test", "raw"))),
            options);

        var preflight = Assert.Single(snapshot.Candidates).Preflight;
        Assert.NotNull(preflight);
        Assert.False(preflight.IsStable);
        Assert.Equal("观察中", preflight.StatusText);
        Assert.Contains("未命名会话｜1｜摘要｜16:13", preflight.Fingerprint, StringComparison.Ordinal);
        Assert.Contains("观察中 1 个", snapshot.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_ShouldNeverSkipVisibleBadgeCandidateWhenOcrTextIsEmpty()
    {
        var tracker = new UnreadConversationQueueStabilityTracker();
        var options = CreateOptions();
        var snapshot = tracker.Apply(
            CreateSnapshot(
                DateTimeOffset.Parse("2026-08-13T10:00:00Z"),
                CreateCandidate(textInfo: new UnreadConversationTextInfo(string.Empty, string.Empty, string.Empty, string.Empty, 0m, "Test", string.Empty))),
            options);

        var preflight = Assert.Single(snapshot.Candidates).Preflight;
        Assert.NotNull(preflight);
        Assert.False(preflight.IsStable);
        Assert.Equal("观察中", preflight.StatusText);
        Assert.Contains("OCR 文本为空", preflight.Reason, StringComparison.Ordinal);
        Assert.Contains("观察中 1 个", snapshot.Summary, StringComparison.Ordinal);
        Assert.Contains("跳过 0 个", snapshot.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_ShouldAcceptCandidateWhenUnreadCountOcrIsMissing()
    {
        var tracker = new UnreadConversationQueueStabilityTracker();
        var options = CreateOptions();
        var snapshot = tracker.Apply(
            CreateSnapshot(
                DateTimeOffset.Parse("2026-08-13T10:00:00Z"),
                CreateCandidate(textInfo: new UnreadConversationTextInfo("贺志奇", "啥整的半自动的这种多久...", "14:26", string.Empty, 0.8m, "Test", "raw"))),
            options);

        var preflight = Assert.Single(snapshot.Candidates).Preflight;
        Assert.NotNull(preflight);
        Assert.False(preflight.IsStable);
        Assert.Equal("观察中", preflight.StatusText);
        Assert.Equal(1, preflight.StableScanCount);
        Assert.Contains("观察中 1 个", snapshot.Summary, StringComparison.Ordinal);
        Assert.Contains("贺志奇｜数字角标", preflight.Fingerprint, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_ShouldLeaveSnapshotUntouchedWhenPreflightDisabled()
    {
        var tracker = new UnreadConversationQueueStabilityTracker();
        var options = CreateOptions();
        options.EnableUnreadQueueReadOnlyPreflight = false;
        var original = CreateSnapshot(DateTimeOffset.Parse("2026-08-13T10:00:00Z"), CreateCandidate());
        var snapshot = tracker.Apply(original, options);

        Assert.Same(original, snapshot);
        Assert.Null(Assert.Single(snapshot.Candidates).Preflight);
    }

    private static RpaAutomationOptions CreateOptions()
    {
        return new RpaAutomationOptions
        {
            EnableUnreadQueueReadOnlyPreflight = true,
            UnreadQueueRequiredStableScanCount = 2,
            UnreadQueueStableRowTolerancePixels = 12,
            UnreadQueueStabilityCacheMinutes = 5
        };
    }

    private static UnreadConversationQueueSnapshot CreateSnapshot(DateTimeOffset scannedAtUtc, UnreadConversationCandidate candidate)
    {
        return new UnreadConversationQueueSnapshot(scannedAtUtc, new Rectangle(0, 0, 272, 1568), [candidate], "扫描完成。", null);
    }

    private static UnreadConversationCandidate CreateCandidate(int rowTop = 100, UnreadConversationTextInfo? textInfo = null)
    {
        return new UnreadConversationCandidate(
            VisualOrder: 0,
            BadgeBounds: new Rectangle(53, rowTop + 18, 18, 18),
            RowBounds: new Rectangle(0, rowTop, 272, 84),
            UnreadHint: "数字未读候选",
            Confidence: 0.90m,
            Source: "Test",
            TextInfo: textInfo ?? new UnreadConversationTextInfo("微信支付", "[2条] 已扣费21.37", "12:56", "2", 0.85m, "TestOcr", "raw"));
    }
}
