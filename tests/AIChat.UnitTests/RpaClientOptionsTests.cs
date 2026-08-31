using AIChat.RpaClient.Configuration;

namespace AIChat.UnitTests;

public sealed class RpaClientOptionsTests
{
    [Fact]
    public void AutomationDefaults_ShouldReviewLatestSelfMessageForContinuousTranscriptAccuracy()
    {
        var options = new RpaAutomationOptions();

        Assert.Equal(RpaSendMode.InputOnly, options.SendMode);
        Assert.Equal(RpaInputOnlyAfterVerifyAction.ClearInput, options.InputOnlyAfterVerifyAction);
        Assert.Equal(1, options.InputVerifyRetryCount);
        Assert.Equal(300, options.InputVerifyDelayMs);
        Assert.True(options.EnableKeyboardFallbackOnClipboardFailure);
        Assert.True(options.ContinuousReviewLatestSelfMessage);
        Assert.True(options.EnableContinuousVisualCache);
        Assert.True(options.EnableContinuousUnchangedFrameSkip);
        Assert.Equal("AllRecognizedMessages", options.ContinuousVisionReviewScope);
        Assert.True(options.EnableWindowTargetLock);
        Assert.Equal(8, options.WindowTargetLockClientBoundsTolerancePixels);
        Assert.True(options.EnableUnreadQueueReadOnlyScan);
        Assert.Equal(6, options.UnreadQueueScanIntervalSeconds);
        Assert.Equal(8, options.MaxUnreadQueueCandidates);
        Assert.Equal(0.50m, options.UnreadQueueMinConfidence);
        Assert.True(options.EnableUnreadQueueDebugCaptures);
        Assert.True(options.EnableUnreadQueueReadOnlyPreflight);
        Assert.Equal(2, options.UnreadQueueRequiredStableScanCount);
        Assert.Equal(12, options.UnreadQueueStableRowTolerancePixels);
        Assert.Equal(5, options.UnreadQueueStabilityCacheMinutes);
        Assert.False(options.EnableUnreadQueueControlledSwitch);
        Assert.Equal(800, options.UnreadQueueSwitchPostClickVerifyDelayMs);
        Assert.True(options.EnableUnreadQueuePostSwitchMessageVerify);
        Assert.Equal(2, options.UnreadQueuePostSwitchMessageVerifyMinChars);
    }

    [Fact]
    public void SendMode_ShouldExposeExpectedExecutionSemantics()
    {
        Assert.False(RpaSendMode.DryRun.ShouldInputReply());
        Assert.False(RpaSendMode.DryRun.ShouldClickSend());

        Assert.True(RpaSendMode.InputOnly.ShouldInputReply());
        Assert.False(RpaSendMode.InputOnly.ShouldClickSend());

        Assert.True(RpaSendMode.RealSendTest.ShouldInputReply());
        Assert.True(RpaSendMode.RealSendTest.ShouldClickSend());
        Assert.False(RpaSendMode.RealSendTest.RequiresProductionGuard());

        Assert.True(RpaSendMode.ProductionGuarded.ShouldInputReply());
        Assert.True(RpaSendMode.ProductionGuarded.ShouldClickSend());
        Assert.True(RpaSendMode.ProductionGuarded.RequiresProductionGuard());
    }

    [Fact]
    public void InputOnlyAfterVerifyAction_ShouldExposeReadableDisplayText()
    {
        Assert.Equal("保留输入框草稿", RpaInputOnlyAfterVerifyAction.KeepDraft.ToDisplayText());
        Assert.Equal("输入校验后清空输入框", RpaInputOnlyAfterVerifyAction.ClearInput.ToDisplayText());
        Assert.Equal("输入校验后全选草稿", RpaInputOnlyAfterVerifyAction.SelectAllOnly.ToDisplayText());
    }
}
