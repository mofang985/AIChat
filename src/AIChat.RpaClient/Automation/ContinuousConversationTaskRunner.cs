using System.Diagnostics;
using AIChat.RpaClient.Backend;
using AIChat.RpaClient.Configuration;

namespace AIChat.RpaClient.Automation;

public sealed class ContinuousConversationTaskRunner(
    RpaBackendClient backendClient,
    WeChatWindowLocator windowLocator,
    ScreenCaptureService screenCaptureService,
    WeChatLayoutDetector layoutDetector,
    YoloOnnxVisionDetector yoloVisionDetector,
    PaddleOcrEngine ocrEngine,
    VisionOcrReviewer visionOcrReviewer,
    MouseKeyboardExecutor inputExecutor,
    RpaAutomationOptions options,
    RpaTaskRunnerCallbacks callbacks)
{
    private readonly ChatMessageVisualExtractor _chatMessageVisualExtractor = new(screenCaptureService, ocrEngine, visionOcrReviewer);
    private readonly SingleConversationReplyCycleExecutor _cycleExecutor = new(
        backendClient,
        windowLocator,
        screenCaptureService,
        layoutDetector,
        yoloVisionDetector,
        ocrEngine,
        visionOcrReviewer,
        inputExecutor,
        options,
        callbacks);
    private readonly UnreadConversationQueueScanner _unreadQueueScanner = new(screenCaptureService, ocrEngine);
    private ContinuousLayoutCacheEntry? _layoutCache;
    private readonly ChatMessageVisualCache _visualCache = new();
    private ChatMessageVisualCacheScope? _visualCacheScope;
    private WeChatWindowLock? _windowLock;
    private DateTimeOffset? _lastUnreadQueueScanAtUtc;

    public async Task RunAsync(
        Guid clientInstanceId,
        Guid? employeeId,
        Guid? weChatWorkAccountId,
        CancellationToken cancellationToken)
    {
        if (!options.EnableContinuousReply)
        {
            callbacks.SetContinuousStatusText("连续监听未启用");
            callbacks.AppendLog("配置 EnableContinuousReply=false，未启动连续监听。");
            return;
        }

        var startedAtUtc = DateTimeOffset.UtcNow;
        var conversationKey = $"single-continuous-{DateTime.Now:yyyyMMddHHmmss}";
        var state = new ContinuousConversationState(TimeSpan.FromMinutes(Math.Max(0, options.DuplicateMessageSuppressMinutes)));
        _visualCache.Clear();
        _visualCacheScope = null;
        _windowLock = null;
        _lastUnreadQueueScanAtUtc = null;

        callbacks.SetContinuousStatusText("正在解析当前视觉消息流");
        callbacks.SetContinuousReplyCountText($"0/{FormatLimit(options.MaxRepliesPerContinuousSession)}");
        callbacks.SetContinuousFailureCountText("0");
        callbacks.SetContinuousMergeCountdownText("未等待");
        callbacks.SetWindowTargetText(options.EnableWindowTargetLock ? "待锁定" : "未启用窗口锁定");
        callbacks.AppendLog($"连续监听启动，ConversationKey={conversationKey}。");

        var startup = await CaptureLatestSnapshotAsync(cancellationToken);
        ApplyContinuousLatestMessageDisplay(startup);

        var pendingStartupSnapshot = startup?.Snapshot;
        if (pendingStartupSnapshot is not null)
        {
            callbacks.AppendLog("连续监听启动时发现待回复客户消息组，将立即处理当前消息组。");
        }
        else if (startup?.LatestEffectiveMessage is not null)
        {
            callbacks.AppendLog($"连续监听启动时最新有效消息为{startup.LatestEffectiveMessage.SenderDisplayName}，等待客户下一条消息。");
        }
        else
        {
            callbacks.AppendLog("连续监听启动时未识别到有效消息，等待客户下一条消息。");
        }

        try
        {
            var startupSnapshotProcessed = false;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var stopReason = GetLimitStopReason(state, startedAtUtc, DateTimeOffset.UtcNow);
                if (stopReason is not null)
                {
                    StopContinuousListening(stopReason, state);
                    return;
                }

                ContinuousPollSnapshot? currentPoll;
                if (!startupSnapshotProcessed && pendingStartupSnapshot is not null)
                {
                    startupSnapshotProcessed = true;
                    currentPoll = startup;
                    callbacks.SetContinuousLastPollText(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    callbacks.SetContinuousStatusText("处理启动时未回复消息");
                    callbacks.AppendLog("连续监听启动后立即处理当前未回复客户消息组。");
                }
                else
                {
                    callbacks.SetContinuousStatusText("等待新客户消息");
                    await Task.Delay(TimeSpan.FromSeconds(NormalizePositive(options.ContinuousPollIntervalSeconds)), cancellationToken);

                    callbacks.SetContinuousLastPollText(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    callbacks.AppendLog("连续监听轮询开始。");

                    var current = await CaptureLatestSnapshotAsync(cancellationToken);
                    ApplyContinuousLatestMessageDisplay(current);
                    if (current?.Snapshot is null && current?.LatestEffectiveMessage is not null)
                    {
                        callbacks.SetContinuousStatusText($"最新有效消息为{current.LatestEffectiveMessage.SenderDisplayName}，等待客户新消息");
                        callbacks.AppendLog($"连续监听跳过：最新有效消息为{current.LatestEffectiveMessage.SenderDisplayName}。");
                        continue;
                    }

                    currentPoll = current;
                }

                var decision = state.Evaluate(currentPoll?.Snapshot, DateTimeOffset.UtcNow);
                if (!decision.ShouldReply)
                {
                    callbacks.SetContinuousStatusText(decision.Reason);
                    callbacks.AppendLog($"连续监听跳过：{decision.Reason}");
                    continue;
                }

                var stablePoll = await WaitForMergedSnapshotAsync(
                    currentPoll!,
                    startedAtUtc,
                    cancellationToken);
                if (stablePoll?.Snapshot is null)
                {
                    callbacks.SetContinuousStatusText("本轮消息已无需自动回复");
                    callbacks.AppendLog("连续监听合并窗口内最新消息不再是客户消息，取消本轮自动回复。");
                    continue;
                }

                var stableSnapshot = stablePoll.Snapshot;
                var stableDecision = state.Evaluate(stableSnapshot, DateTimeOffset.UtcNow);
                if (!stableDecision.ShouldReply)
                {
                    callbacks.SetContinuousStatusText(stableDecision.Reason);
                    callbacks.AppendLog($"连续监听合并后跳过：{stableDecision.Reason}");
                    continue;
                }

                callbacks.SetContinuousMergeCountdownText("已稳定");
                callbacks.SetContinuousStatusText("开始本轮自动回复");
                callbacks.AppendLog($"连续监听检测到待回复客户消息组：{stableSnapshot.LatestMessage}");

                var result = await _cycleExecutor.ExecuteAsync(
                    new SingleConversationReplyCycleRequest(
                        clientInstanceId,
                        employeeId,
                        weChatWorkAccountId,
                        conversationKey,
                        "当前会话",
                        true,
                        stablePoll.Window,
                        stablePoll.Layout,
                        stablePoll.VisualMessages,
                        stablePoll.WindowLock),
                    cancellationToken);

                if (result.Succeeded)
                {
                    var repliedSnapshot = result.Snapshot ?? stableSnapshot;
                    state.RecordReplySuccess(repliedSnapshot, DateTimeOffset.UtcNow);
                    callbacks.SetContinuousReplyCountText($"{state.ReplyCount}/{FormatLimit(options.MaxRepliesPerContinuousSession)}");
                    callbacks.SetContinuousFailureCountText(state.ConsecutiveFailureCount.ToString());
                    callbacks.SetContinuousStatusText("本轮回复成功，继续监听");
                    callbacks.AppendLog("连续监听本轮回复成功，继续等待下一条客户消息。");
                    continue;
                }

                state.RecordReplyFailure();
                callbacks.SetContinuousFailureCountText(state.ConsecutiveFailureCount.ToString());

                var failureReason = result.FailureReason ?? "本轮自动回复未成功。";
                callbacks.AppendLog($"连续监听本轮回复未成功：{failureReason}");

                if (result.TaskStatus.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    StopContinuousListening("员工暂停或紧急停止。", state);
                    return;
                }

                if (options.StopContinuousOnManualReviewRequired && result.RequiresManualReview)
                {
                    StopContinuousListening($"需要人工处理：{failureReason}", state);
                    return;
                }

                if (result.TaskStatus.Equals("Skipped", StringComparison.OrdinalIgnoreCase))
                {
                    state.RecordReplySkipped(result.Snapshot ?? stableSnapshot);
                    callbacks.SetContinuousFailureCountText(state.ConsecutiveFailureCount.ToString());
                    callbacks.SetContinuousStatusText("Current message skipped; continue listening for a new customer message.");
                    callbacks.AppendLog("Continuous reply skipped current message and will keep listening.");
                    continue;
                }

                if (options.StopContinuousOnSendFailure && result.IsSendFailure)
                {
                    StopContinuousListening($"发送失败，停止连续监听：{failureReason}", state);
                    return;
                }

                if (state.HasReachedMaxFailures(options.MaxConsecutiveContinuousFailures))
                {
                    StopContinuousListening($"连续失败达到 {options.MaxConsecutiveContinuousFailures} 次。", state);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            StopContinuousListening("员工暂停或紧急停止。", state);
        }
        catch (Exception ex)
        {
            state.RecordReplyFailure();
            callbacks.SetContinuousFailureCountText(state.ConsecutiveFailureCount.ToString());
            StopContinuousListening($"连续监听异常：{ex.Message}", state);
        }
        finally
        {
            callbacks.SetContinuousMergeCountdownText("未等待");
        }
    }

    private async Task<ContinuousPollSnapshot?> CaptureLatestSnapshotAsync(CancellationToken cancellationToken)
    {
        callbacks.SetStatus("正在定位窗口");
        callbacks.SetContinuousStatusText("正在定位窗口");
        var windowStopwatch = Stopwatch.StartNew();
        var window = ResolveContinuousWindow();
        callbacks.AppendLog($"[性能] 连续监听窗口定位完成，耗时 {windowStopwatch.ElapsedMilliseconds} ms，客户区 {window.ClientBounds}，目标 {FormatWindowTarget(window)}。");

        windowLocator.Activate(window);
        var layout = await GetOrDetectLayoutAsync(window, cancellationToken);

        callbacks.SetLayoutStatus($"{layout.Mode} / {layout.Confidence:P0} / 连续监听轮询");
        if (!layout.IsUsable)
        {
            throw new InvalidOperationException($"微信布局定位失败：{layout.Reason}");
        }
        ResetVisualCacheIfScopeChanged(window, layout);
        await RefreshUnreadQueueIfDueAsync(layout, cancellationToken);

        callbacks.SetStatus("正在识别底部最近气泡");
        callbacks.SetContinuousStatusText("正在识别底部最近气泡");
        var visualStopwatch = Stopwatch.StartNew();
        var visualMessages = await _chatMessageVisualExtractor.ExtractAsync(
            layout.ConversationContextRegion,
            options,
            Guid.NewGuid(),
            cancellationToken,
            saveDebugCapture: options.EnableDebugCaptures,
            extractionOptions: CreateContinuousExtractionOptions());
        callbacks.AppendLog($"连续监听视觉消息流：{visualMessages.Summary}");
        callbacks.AppendLog($"[性能] 连续监听视觉消息流解析完成，耗时 {visualStopwatch.ElapsedMilliseconds} ms。");

        if (visualMessages.LatestEffectiveMessage?.SenderType == ChatMessageSenderType.Unknown)
        {
            callbacks.AppendLog("Vision OCR did not confirm latest message sender; skip this poll and keep listening.");
            return new ContinuousPollSnapshot(
                null,
                visualMessages.OcrConfidence,
                visualMessages.LatestEffectiveMessage,
                visualMessages.Messages,
                window,
                _windowLock,
                layout,
                visualMessages);
        }

        if (visualMessages.LatestEffectiveMessage is not null)
        {
            var visualSnapshot = visualMessages.CustomerSnapshot;
            if (visualSnapshot is null)
            {
                callbacks.SetOcrText(string.Empty);
                return new ContinuousPollSnapshot(
                    null,
                    visualMessages.OcrConfidence,
                    visualMessages.LatestEffectiveMessage,
                    visualMessages.Messages,
                    window,
                    _windowLock,
                    layout,
                    visualMessages);
            }

            callbacks.SetOcrText(visualSnapshot.LatestMessage);
            var visualConfidence = CalculateOcrConfidence(visualMessages.PendingCustomerMessageGroup?.Messages) ??
                visualMessages.LatestEffectiveMessage?.OcrConfidence ??
                visualMessages.OcrConfidence;
            if (visualConfidence < options.OcrMinConfidence)
            {
                callbacks.AppendLog($"Continuous OCR confidence is below threshold after vision review: {visualConfidence:P0}; skip this poll and keep listening.");
                return new ContinuousPollSnapshot(
                    null,
                    visualConfidence,
                    visualMessages.LatestEffectiveMessage,
                    visualMessages.Messages,
                    window,
                    _windowLock,
                    layout,
                    visualMessages);
            }

            return new ContinuousPollSnapshot(
                visualSnapshot,
                visualConfidence,
                visualMessages.LatestEffectiveMessage,
                visualMessages.Messages,
                window,
                _windowLock,
                layout,
                visualMessages);
        }

        callbacks.SetOcrText(string.Empty);
        callbacks.AppendLog("连续监听视觉消息流未识别到有效消息，本轮不使用左侧客户 OCR 做回复判断。");
        return new ContinuousPollSnapshot(
            null,
            visualMessages.OcrConfidence,
            null,
            visualMessages.Messages,
            window,
            _windowLock,
            layout,
            visualMessages);
    }

    private async Task<ContinuousPollSnapshot?> WaitForMergedSnapshotAsync(
        ContinuousPollSnapshot initialSnapshot,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken)
    {
        var stableSnapshot = initialSnapshot;
        var mergeWindow = TimeSpan.FromSeconds(Math.Max(0, options.MessageMergeWindowSeconds));
        if (mergeWindow <= TimeSpan.Zero)
        {
            callbacks.SetContinuousMergeCountdownText("未等待");
            callbacks.AppendLog("消息合并窗口为 0 秒，直接使用当前客户消息组。");
            return stableSnapshot;
        }

        callbacks.SetContinuousStatusText("等待客户消息稳定");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var waitStartedAtUtc = DateTimeOffset.UtcNow;
            while (DateTimeOffset.UtcNow - waitStartedAtUtc < mergeWindow)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var stopReason = GetLimitStopReason(null, startedAtUtc, DateTimeOffset.UtcNow);
                if (stopReason is not null)
                {
                    callbacks.AppendLog($"合并窗口提前结束：{stopReason}");
                    return stableSnapshot;
                }

                var remaining = mergeWindow - (DateTimeOffset.UtcNow - waitStartedAtUtc);
                callbacks.SetContinuousMergeCountdownText($"{Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds))} 秒");
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(1, Math.Max(0.1, remaining.TotalSeconds))), cancellationToken);
            }

            var current = await CaptureLatestSnapshotAsync(cancellationToken);
            if (current?.Snapshot is null)
            {
                if (current?.LatestEffectiveMessage is not null)
                {
                    callbacks.AppendLog($"合并窗口内最新有效消息变为{current.LatestEffectiveMessage.SenderDisplayName}，取消本轮回复。");
                    return null;
                }

                callbacks.AppendLog("合并窗口结束复查时未识别到新的有效消息，沿用上一轮稳定客户消息组。");
                return stableSnapshot;
            }

            if (current.Snapshot.Fingerprint == stableSnapshot.Snapshot?.Fingerprint)
            {
                callbacks.AppendLog("合并窗口结束复查后客户消息组未变化。");
                return current;
            }

            stableSnapshot = current;
            callbacks.SetContinuousLatestMessageText(stableSnapshot.Snapshot.LatestMessage);
            callbacks.AppendLog("客户消息在合并窗口内发生变化，重新等待一个合并窗口。");
        }
    }

    private WeChatWindow ResolveContinuousWindow()
    {
        if (!options.EnableWindowTargetLock)
        {
            callbacks.SetWindowTargetText("未启用窗口锁定");
            return windowLocator.FindByTitleKeyword(options.WeChatWindowTitleKeyword)
                ?? throw new InvalidOperationException("未找到微信窗口，连续监听已停止。");
        }

        if (_windowLock is null)
        {
            var window = windowLocator.FindByTitleKeyword(options.WeChatWindowTitleKeyword)
                ?? throw new InvalidOperationException("未找到微信窗口，连续监听已停止。");
            _windowLock = WeChatWindowLock.Capture(window, DateTimeOffset.UtcNow);
            callbacks.SetWindowTargetText(_windowLock.ToDisplayText());
            callbacks.AppendLog($"连续监听已锁定微信窗口：{_windowLock.ToDisplayText()}。");
            return window;
        }

        var currentWindow = windowLocator.FindByHandle(_windowLock.Handle);
        var validation = _windowLock.Validate(currentWindow, options.WindowTargetLockClientBoundsTolerancePixels);
        if (!validation.IsValid)
        {
            callbacks.SetWindowTargetText($"锁定失效：{_windowLock.ToDisplayText()}");
            throw new InvalidOperationException($"微信窗口锁定校验失败：{validation.Reason}");
        }

        callbacks.SetWindowTargetText(_windowLock.ToDisplayText());
        return validation.CurrentWindow!;
    }

    private static string FormatWindowTarget(WeChatWindow window)
    {
        return $"Handle=0x{window.Handle.ToInt64():X}, Monitor={FormatRectangle(window.MonitorBounds)}, DPI={(window.Dpi == 0 ? "未知" : window.Dpi.ToString())}";
    }

    private static string FormatRectangle(System.Drawing.Rectangle rectangle)
    {
        return $"X={rectangle.X},Y={rectangle.Y},W={rectangle.Width},H={rectangle.Height}";
    }

    private async Task<WeChatLayoutResult> GetOrDetectLayoutAsync(
        WeChatWindow window,
        CancellationToken cancellationToken)
    {
        if (options.EnableContinuousLayoutCache &&
            _layoutCache is not null &&
            _layoutCache.WindowHandle == window.Handle &&
            _layoutCache.ClientBounds == window.ClientBounds &&
            _layoutCache.Layout.IsUsable)
        {
            callbacks.SetContinuousStatusText("布局缓存命中");
            callbacks.AppendLog($"[性能] 连续监听布局缓存命中，跳过布局检测，客户区 {window.ClientBounds}。");
            return _layoutCache.Layout;
        }

        callbacks.SetStatus("正在布局检测");
        callbacks.SetContinuousStatusText("正在布局检测");
        var layoutStopwatch = Stopwatch.StartNew();
        var layout = await layoutDetector.DetectAsync(
            window,
            options,
            Guid.NewGuid(),
            cancellationToken,
            saveDebugCapture: false);
        callbacks.AppendLog($"[性能] 连续监听布局检测完成，耗时 {layoutStopwatch.ElapsedMilliseconds} ms。");

        if (layout.IsUsable)
        {
            _layoutCache = new ContinuousLayoutCacheEntry(window.Handle, window.ClientBounds, layout);
        }

        return layout;
    }

    private async Task RefreshUnreadQueueIfDueAsync(WeChatLayoutResult layout, CancellationToken cancellationToken)
    {
        if (!options.EnableUnreadQueueReadOnlyScan)
        {
            return;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var interval = TimeSpan.FromSeconds(NormalizePositive(options.UnreadQueueScanIntervalSeconds));
        if (_lastUnreadQueueScanAtUtc is not null && nowUtc - _lastUnreadQueueScanAtUtc < interval)
        {
            return;
        }

        _lastUnreadQueueScanAtUtc = nowUtc;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (layout.ConversationListRegion.IsEmpty || layout.ConversationListRegion.Width <= 0 || layout.ConversationListRegion.Height <= 0)
            {
                callbacks.SetUnreadQueueSnapshotValue(UnreadConversationQueueSnapshot.Empty(
                    nowUtc,
                    layout.ConversationListRegion,
                    "当前布局没有可用会话列表区域，未读队列只读扫描已跳过。"));
                callbacks.AppendLog("未读队列只读扫描跳过：当前布局没有可用会话列表区域。");
                return;
            }

            var snapshot = await _unreadQueueScanner.ScanAsync(
                layout,
                options,
                Guid.NewGuid(),
                cancellationToken);
            callbacks.SetUnreadQueueSnapshotValue(snapshot);
            callbacks.AppendLog($"未读队列只读扫描完成：{snapshot.Summary}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            callbacks.SetUnreadQueueSnapshotValue(UnreadConversationQueueSnapshot.Empty(
                nowUtc,
                layout.ConversationListRegion,
                $"未读队列只读扫描异常：{ex.Message}"));
            callbacks.AppendLog($"未读队列只读扫描异常：{ex.Message}");
        }
    }

    private ChatMessageVisualExtractionOptions CreateContinuousExtractionOptions()
    {
        var reviewOnlyPendingGroup = string.Equals(
            options.ContinuousVisionReviewScope,
            "PendingCustomerGroupOnly",
            StringComparison.OrdinalIgnoreCase);

        return new ChatMessageVisualExtractionOptions(
            Math.Max(0, options.ContinuousMaxVisualMessagesToOcr),
            Math.Clamp(options.ContinuousVisualOcrBottomRatio, 0.10m, 1.00m),
            reviewOnlyPendingGroup,
            options.EnablePerformanceDiagnostics ? callbacks.AppendLog : null,
            status =>
            {
                callbacks.SetStatus(status);
                callbacks.SetContinuousStatusText(status);
            },
            options.EnableContinuousVisualCache ? _visualCache : null,
            options.EnableContinuousUnchangedFrameSkip,
            Math.Clamp(options.ContinuousUnchangedFrameBottomRatio, 0.10m, 1.00m),
            options.ContinuousDebugCaptureMode);
    }

    private void ResetVisualCacheIfScopeChanged(WeChatWindow window, WeChatLayoutResult layout)
    {
        if (!options.EnableContinuousVisualCache)
        {
            return;
        }

        var currentScope = new ChatMessageVisualCacheScope(
            window.Handle,
            window.ClientBounds,
            layout.ConversationContextRegion);
        if (_visualCacheScope == currentScope)
        {
            return;
        }

        _visualCache.Clear();
        _visualCacheScope = currentScope;
        callbacks.AppendLog($"[性能] 连续监听视觉缓存作用域已重置，窗口={window.Handle}，聊天区={layout.ConversationContextRegion}。");
    }

    private string? GetLimitStopReason(
        ContinuousConversationState? state,
        DateTimeOffset startedAtUtc,
        DateTimeOffset nowUtc)
    {
        if (ContinuousConversationState.HasReachedSessionDeadline(startedAtUtc, nowUtc, options.MaxContinuousSessionMinutes))
        {
            return $"连续监听达到最大时长 {options.MaxContinuousSessionMinutes} 分钟。";
        }

        if (state is not null && state.HasReachedMaxReplies(options.MaxRepliesPerContinuousSession))
        {
            return $"连续监听达到最大回复次数 {options.MaxRepliesPerContinuousSession}。";
        }

        if (state is not null && state.HasReachedMaxFailures(options.MaxConsecutiveContinuousFailures))
        {
            return $"连续失败达到 {options.MaxConsecutiveContinuousFailures} 次。";
        }

        return null;
    }

    private void ApplyContinuousLatestMessageDisplay(ContinuousPollSnapshot? snapshot)
    {
        callbacks.SetContinuousLatestSenderText(snapshot?.LatestEffectiveMessage?.SenderDisplayName ?? "无");
        callbacks.SetContinuousLatestMessageText(
            snapshot?.Snapshot?.LatestMessage ??
            snapshot?.LatestEffectiveMessage?.Text ??
            "未识别到有效消息");
    }

    private void StopContinuousListening(string reason, ContinuousConversationState state)
    {
        callbacks.SetStatus("连续监听已停止");
        callbacks.SetError(reason);
        callbacks.SetContinuousStatusText(reason);
        callbacks.SetContinuousReplyCountText($"{state.ReplyCount}/{FormatLimit(options.MaxRepliesPerContinuousSession)}");
        callbacks.SetContinuousFailureCountText(state.ConsecutiveFailureCount.ToString());
        callbacks.AppendLog($"连续监听停止：{reason}");
    }

    private static int NormalizePositive(int value)
    {
        return Math.Max(1, value);
    }

    private static string FormatLimit(int value)
    {
        return value <= 0 ? "不限" : value.ToString();
    }

    private static decimal? CalculateOcrConfidence(IReadOnlyList<ChatMessageItem>? messages)
    {
        var values = messages?
            .Select(message => message.OcrConfidence)
            .Where(confidence => confidence > 0)
            .ToArray();

        return values is { Length: > 0 } ? values.Average() : null;
    }
}

public sealed record ContinuousPollSnapshot(
    CustomerMessageSnapshot? Snapshot,
    decimal OcrConfidence,
    ChatMessageItem? LatestEffectiveMessage,
    IReadOnlyList<ChatMessageItem> Messages,
    WeChatWindow Window,
    WeChatWindowLock? WindowLock,
    WeChatLayoutResult Layout,
    ChatMessageVisualExtractionResult VisualMessages);

public sealed record ContinuousLayoutCacheEntry(
    IntPtr WindowHandle,
    System.Drawing.Rectangle ClientBounds,
    WeChatLayoutResult Layout);
