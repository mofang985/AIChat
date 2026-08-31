using System.Drawing;

namespace AIChat.RpaClient.Configuration;

public sealed class RpaClientOptions
{
    public string ApiBaseUrl { get; set; } = "https://localhost:7001";
    public string ClientInstanceKey { get; set; } = string.Empty;
    public Guid? EmployeeId { get; set; }
    public Guid? VirtualDeviceId { get; set; }
    public Guid? WeChatWorkAccountId { get; set; }
    public string ClientVersion { get; set; } = "0.5.5-m55";
    public RpaAutomationOptions Automation { get; set; } = new();
}

public sealed class RpaAutomationOptions
{
    public string WeChatWindowTitleKeyword { get; set; } = "微信";
    public string CoordinateMode { get; set; } = "AutoWithManualFallback";
    public FixedRectangle IncomingMessageRegion { get; set; } = new(300, 70, 1300, 720);
    public FixedPoint InputClickPoint { get; set; } = new(520, -130);
    public FixedRectangle InputVerifyRegion { get; set; } = new(300, -220, 1600, 200);
    public FixedPoint SendButtonPoint { get; set; } = new(-55, -34);
    public int ReviewDelaySeconds { get; set; } = 3;
    public int MinSendIntervalSeconds { get; set; } = 8;
    public decimal OcrMinConfidence { get; set; } = 0.65m;
    public RpaSendMode SendMode { get; set; } = RpaSendMode.InputOnly;
    public RpaInputOnlyAfterVerifyAction InputOnlyAfterVerifyAction { get; set; } = RpaInputOnlyAfterVerifyAction.ClearInput;
    public int InputVerifyRetryCount { get; set; } = 1;
    public int InputVerifyDelayMs { get; set; } = 300;
    public bool EnableKeyboardFallbackOnClipboardFailure { get; set; } = true;
    public bool EnableDebugCaptures { get; set; }
    public string? DebugCaptureDirectory { get; set; }
    public bool EnableLayoutDebugCaptures { get; set; } = true;
    public string? LayoutDebugCaptureDirectory { get; set; }
    public decimal LayoutDetectionMinConfidence { get; set; } = 0.65m;
    public bool EnableYoloLayoutValidation { get; set; }
    public string? YoloModelPath { get; set; }
    public string? YoloLabelsPath { get; set; }
    public int YoloInputSize { get; set; } = 640;
    public decimal YoloMinConfidence { get; set; } = 0.60m;
    public decimal YoloNmsThreshold { get; set; } = 0.45m;
    public bool EnableYoloDebugCaptures { get; set; } = true;
    public string? YoloDebugCaptureDirectory { get; set; }
    public bool EnableVisionOcrReview { get; set; } = true;
    public string VisionOcrProvider { get; set; } = "Ollama";
    public string VisionOcrBaseUrl { get; set; } = "http://localhost:11434";
    public string VisionOcrModel { get; set; } = "qwen2.5vl:7b";
    public string VisionReviewMode { get; set; } = "AlwaysForCustomerMessages";
    public int VisionOcrTimeoutSeconds { get; set; } = 8;
    public decimal VisionOcrMinConfidence { get; set; } = 0.70m;
    public string VisionOcrFailureBehavior { get; set; } = "SkipAndContinue";
    public bool EnableVisionOcrDebugCaptures { get; set; } = true;
    public string? VisionOcrDebugCaptureDirectory { get; set; }
    public string InputMode { get; set; } = "ClipboardPaste";
    public string? ProviderCode { get; set; }
    public string? PromptTemplateCode { get; set; }
    public int MaxKnowledgeResults { get; set; } = 5;
    public int ClickWaitMs { get; set; } = 300;
    public int KeyboardWaitMs { get; set; } = 20;
    public bool HumanizeInput { get; set; } = true;
    public int MouseMoveDurationMsMin { get; set; } = 180;
    public int MouseMoveDurationMsMax { get; set; } = 520;
    public int MouseMoveStepsMin { get; set; } = 8;
    public int MouseMoveStepsMax { get; set; } = 22;
    public int MouseMoveJitterPixels { get; set; } = 3;
    public int ClickJitterPixels { get; set; } = 6;
    public int ClickDownMsMin { get; set; } = 45;
    public int ClickDownMsMax { get; set; } = 120;
    public int KeyPressMsMin { get; set; } = 25;
    public int KeyPressMsMax { get; set; } = 75;
    public int KeyDelayMsMin { get; set; } = 35;
    public int KeyDelayMsMax { get; set; } = 120;
    public decimal TypingPauseChance { get; set; } = 0.08m;
    public int TypingPauseMsMin { get; set; } = 180;
    public int TypingPauseMsMax { get; set; } = 520;
    public bool EnableLearningSampleCapture { get; set; }
    public string? LearningSampleDirectory { get; set; }
    public decimal LearningSampleMinReviewConfidence { get; set; } = 0.85m;
    public int LearningSampleRetentionDays { get; set; } = 14;
    public bool IncludeLearningSampleText { get; set; }
    public bool EnableContinuousReply { get; set; }
    public int ContinuousPollIntervalSeconds { get; set; } = 3;
    public int MessageMergeWindowSeconds { get; set; } = 5;
    public int MaxContinuousSessionMinutes { get; set; } = 30;
    public int MaxRepliesPerContinuousSession { get; set; } = 20;
    public int MaxConsecutiveContinuousFailures { get; set; } = 3;
    public int DuplicateMessageSuppressMinutes { get; set; } = 10;
    public string ContinuousStartMode { get; set; } = "VisualLatestMessage";
    public string ReplyGroupingMode { get; set; } = "Combined";
    public bool StopContinuousOnManualReviewRequired { get; set; } = true;
    public bool StopContinuousOnSendFailure { get; set; } = true;
    public bool EnablePerformanceDiagnostics { get; set; } = true;
    public int ContinuousMaxVisualMessagesToOcr { get; set; } = 8;
    public decimal ContinuousVisualOcrBottomRatio { get; set; } = 0.60m;
    public string ContinuousVisionReviewScope { get; set; } = "AllRecognizedMessages";
    public bool EnableContinuousLayoutCache { get; set; } = true;
    public bool EnableContinuousVisualCache { get; set; } = true;
    public int ContinuousVisualCacheMaxEntries { get; set; } = 80;
    public bool EnableContinuousUnchangedFrameSkip { get; set; } = true;
    public decimal ContinuousUnchangedFrameBottomRatio { get; set; } = 0.45m;
    public string ContinuousDebugCaptureMode { get; set; } = "OnError";
    public bool ContinuousReviewLatestSelfMessage { get; set; } = true;
    public bool EnableWindowTargetLock { get; set; } = true;
    public int WindowTargetLockClientBoundsTolerancePixels { get; set; } = 8;
    public bool EnableUnreadQueueReadOnlyScan { get; set; } = true;
    public int UnreadQueueScanIntervalSeconds { get; set; } = 6;
    public int MaxUnreadQueueCandidates { get; set; } = 8;
    public decimal UnreadQueueMinConfidence { get; set; } = 0.50m;
    public bool EnableUnreadQueueDebugCaptures { get; set; } = true;
    public string? UnreadQueueDebugCaptureDirectory { get; set; }
    public bool EnableUnreadQueueReadOnlyPreflight { get; set; } = true;
    public int UnreadQueueRequiredStableScanCount { get; set; } = 2;
    public int UnreadQueueStableRowTolerancePixels { get; set; } = 12;
    public int UnreadQueueStabilityCacheMinutes { get; set; } = 5;
    public bool EnableUnreadQueueControlledSwitch { get; set; } = false;
    public int UnreadQueueSwitchPostClickVerifyDelayMs { get; set; } = 800;
    public bool EnableUnreadQueuePostSwitchMessageVerify { get; set; } = true;
    public int UnreadQueuePostSwitchMessageVerifyMinChars { get; set; } = 2;
}

public enum RpaSendMode
{
    DryRun,
    InputOnly,
    RealSendTest,
    ProductionGuarded
}

public enum RpaInputOnlyAfterVerifyAction
{
    KeepDraft,
    ClearInput,
    SelectAllOnly
}

public static class RpaInputOnlyAfterVerifyActionExtensions
{
    public static string ToDisplayText(this RpaInputOnlyAfterVerifyAction action)
    {
        return action switch
        {
            RpaInputOnlyAfterVerifyAction.KeepDraft => "保留输入框草稿",
            RpaInputOnlyAfterVerifyAction.ClearInput => "输入校验后清空输入框",
            RpaInputOnlyAfterVerifyAction.SelectAllOnly => "输入校验后全选草稿",
            _ => action.ToString()
        };
    }
}

public static class RpaSendModeExtensions
{
    public static bool ShouldInputReply(this RpaSendMode sendMode)
    {
        return sendMode is RpaSendMode.InputOnly or RpaSendMode.RealSendTest or RpaSendMode.ProductionGuarded;
    }

    public static bool ShouldClickSend(this RpaSendMode sendMode)
    {
        return sendMode is RpaSendMode.RealSendTest or RpaSendMode.ProductionGuarded;
    }

    public static bool RequiresProductionGuard(this RpaSendMode sendMode)
    {
        return sendMode == RpaSendMode.ProductionGuarded;
    }

    public static string ToDisplayText(this RpaSendMode sendMode)
    {
        return sendMode switch
        {
            RpaSendMode.DryRun => "DryRun：只识别和生成，不输入、不发送",
            RpaSendMode.InputOnly => "InputOnly：输入并校验，不点击发送",
            RpaSendMode.RealSendTest => "RealSendTest：测试真实发送",
            RpaSendMode.ProductionGuarded => "ProductionGuarded：生产防护真实发送",
            _ => sendMode.ToString()
        };
    }

    public static string ToStatusTag(this RpaSendMode sendMode)
    {
        return sendMode switch
        {
            RpaSendMode.DryRun => "SendMode=DryRun",
            RpaSendMode.InputOnly => "SendMode=InputOnly",
            RpaSendMode.RealSendTest => "SendMode=RealSendTest",
            RpaSendMode.ProductionGuarded => "SendMode=ProductionGuarded",
            _ => $"SendMode={sendMode}"
        };
    }
}

public sealed record FixedRectangle(int X, int Y, int Width, int Height)
{
    public Rectangle ToScreenRectangle(Rectangle clientBounds)
    {
        var x = X < 0 ? clientBounds.Right + X : clientBounds.Left + X;
        var y = Y < 0 ? clientBounds.Bottom + Y : clientBounds.Top + Y;
        var width = Math.Min(Width, Math.Max(0, clientBounds.Right - x));
        var height = Math.Min(Height, Math.Max(0, clientBounds.Bottom - y));
        return new Rectangle(x, y, width, height);
    }
}

public sealed record FixedPoint(int X, int Y)
{
    public Point ToScreenPoint(Rectangle clientBounds)
    {
        var x = X < 0 ? clientBounds.Right + X : clientBounds.Left + X;
        var y = Y < 0 ? clientBounds.Bottom + Y : clientBounds.Top + Y;
        return new Point(x, y);
    }
}
