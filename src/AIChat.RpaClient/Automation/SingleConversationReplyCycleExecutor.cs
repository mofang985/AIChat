using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using AIChat.RpaClient.Backend;
using AIChat.RpaClient.Configuration;

namespace AIChat.RpaClient.Automation;

public sealed class SingleConversationReplyCycleExecutor(
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
    private readonly LearningSampleCollector _learningSampleCollector = new(screenCaptureService);
    private readonly ChatMessageVisualExtractor _chatMessageVisualExtractor = new(screenCaptureService, ocrEngine, visionOcrReviewer);

    public Task<SingleConversationReplyCycleResult> RunAsync(
        Guid clientInstanceId,
        Guid? employeeId,
        Guid? weChatWorkAccountId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            new SingleConversationReplyCycleRequest(
                clientInstanceId,
                employeeId,
                weChatWorkAccountId,
                $"single-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
                "当前会话",
                false),
            cancellationToken);
    }

    public async Task<SingleConversationReplyCycleResult> ExecuteAsync(
        SingleConversationReplyCycleRequest request,
        CancellationToken cancellationToken)
    {
        RpaTaskDto? task = null;
        var conversationKey = string.IsNullOrWhiteSpace(request.ConversationKey)
            ? $"single-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}"
            : request.ConversationKey;
        var customerDisplayName = string.IsNullOrWhiteSpace(request.CustomerDisplayName)
            ? "当前会话"
            : request.CustomerDisplayName;
        WeChatWindow? window = null;
        WeChatWindowLock? windowLock = request.WindowLock;
        var sendMode = options.SendMode;
        var inputOnlyAfterVerifyAction = options.InputOnlyAfterVerifyAction;
        WeChatLayoutResult? layout = null;
        YoloLayoutValidationResult? yoloValidation = null;
        CustomerMessageSnapshot? messageSnapshot = null;
        var finalTaskStatus = "Failed";
        string? finalFailureAction = null;
        string? finalFailureReason = null;
        string? ocrTextForSample = null;
        string? aiReplyTextForSample = null;

        try
        {
            callbacks.SetError(string.Empty);
            callbacks.SetCountdown("未开始");
            callbacks.SetStatus("正在创建任务");
            task = await backendClient.CreateTaskAsync(
                new CreateRpaTaskRequest(
                    request.ClientInstanceId,
                    request.EmployeeId,
                    request.WeChatWorkAccountId,
                    "ReplyMessage",
                    100,
                    conversationKey,
                    customerDisplayName,
                    null,
                    null,
                    null,
                    DateTimeOffset.UtcNow),
                cancellationToken);

            await SafeLogAsync(task.Id, "TaskCreated", $"RPA 单会话任务已创建，{sendMode.ToStatusTag()}。", null, null, sendMode.ToStatusTag(), "Info", cancellationToken);
            if (request.IsContinuous)
            {
                await SafeLogAsync(task.Id, "ContinuousReplyCycleStarted", $"连续监听触发本轮回复，ConversationKey={conversationKey}。", null, null, null, "Info", cancellationToken);
            }

            await backendClient.UpdateTaskStatusAsync(task.Id, "Running", null, cancellationToken);
            callbacks.SetStatus($"任务运行中：{task.Id}");

            ChatMessageVisualExtractionResult? visualMessages;
            if (request.Window is not null &&
                request.Layout is not null &&
                request.Layout.IsUsable &&
                request.VisualMessages is not null)
            {
                callbacks.SetStatus("复用连续监听识别结果");
                window = request.Window;
                layout = request.Layout;
                visualMessages = request.VisualMessages;
                callbacks.SetWindowTargetText(windowLock?.ToDisplayText() ?? "未启用窗口锁定");
                yoloValidation = YoloLayoutValidationResult.CreateSkipped("连续监听已提供预识别布局，本轮跳过 YOLO 旁路验证。");
                callbacks.SetLayoutStatus($"{layout.Mode} / {layout.Confidence:P0} / 已复用");
                await SafeLogAsync(
                    task.Id,
                    "ContinuousPreCaptureReused",
                    $"已复用连续监听预识别结果：窗口 {window.Title}，客户区 {window.ClientBounds}，{visualMessages.Summary}。",
                    ChatMessageFlowAnalyzer.FormatConversationContext(visualMessages.Messages),
                    null,
                    $"Messages:{visualMessages.Messages.Count}; OCR:{visualMessages.OcrConfidence:0.0000}",
                    "Info",
                    cancellationToken);
            }
            else
            {
                callbacks.SetStatus("正在定位窗口");
                var windowLocateStopwatch = Stopwatch.StartNew();
                window = windowLocator.FindByTitleKeyword(options.WeChatWindowTitleKeyword)
                    ?? throw new RpaFlowException("未找到微信窗口。", "WindowNotFound", "Error", "Failed");
                callbacks.AppendLog($"[性能] 窗口定位完成，耗时 {windowLocateStopwatch.ElapsedMilliseconds} ms。");

                windowLocator.Activate(window);
                callbacks.AppendLog($"已定位微信窗口：{window.Title}，客户区 {window.ClientBounds}。");
                if (options.EnableWindowTargetLock)
                {
                    windowLock = WeChatWindowLock.Capture(window, DateTimeOffset.UtcNow);
                    callbacks.SetWindowTargetText(windowLock.ToDisplayText());
                    callbacks.AppendLog($"单次任务已锁定微信窗口：{windowLock.ToDisplayText()}。");
                }
                else
                {
                    callbacks.SetWindowTargetText("未启用窗口锁定");
                }
                await SafeLogAsync(task.Id, "WindowLocated", $"已定位微信窗口：{window.Title}，窗口定位耗时 {windowLocateStopwatch.ElapsedMilliseconds} ms。", null, null, null, "Info", cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                callbacks.SetStatus("正在布局检测");
                var layoutStopwatch = Stopwatch.StartNew();
                layout = await layoutDetector.DetectAsync(window, options, task.Id, cancellationToken);
                callbacks.AppendLog($"[性能] 布局检测完成，耗时 {layoutStopwatch.ElapsedMilliseconds} ms。");
                callbacks.SetLayoutStatus($"{layout.Mode} / {layout.Confidence:P0}");
                await SafeLogAsync(
                    task.Id,
                    layout.IsUsable ? "LayoutDetected" : "LayoutDetectionFailed",
                    $"{layout.ToLogMessage()} 布局检测耗时 {layoutStopwatch.ElapsedMilliseconds} ms。",
                    null,
                    null,
                    $"Layout:{layout.Confidence:0.0000}; ElapsedMs:{layoutStopwatch.ElapsedMilliseconds}",
                    layout.IsUsable ? "Info" : "Error",
                    cancellationToken);

                yoloValidation = await RunYoloLayoutValidationAsync(window, layout, task.Id, cancellationToken);

                if (!layout.IsUsable)
                {
                    throw new RpaFlowException(
                        layout.Reason,
                        "LayoutDetectionFailed",
                        "Error",
                        "Failed");
                }

                callbacks.SetStatus("正在识别气泡");
                visualMessages = await CaptureChatMessageFlowAsync(layout.ConversationContextRegion, task.Id, cancellationToken);
            }

            var latestMessageOcr = visualMessages.LatestEffectiveMessage is null
                ? await CaptureLegacyLatestCustomerMessageAsync(layout, task.Id, cancellationToken)
                : null;
            messageSnapshot = visualMessages.CustomerSnapshot ?? latestMessageOcr?.Snapshot;
            var latestEffectiveMessage = visualMessages.LatestEffectiveMessage;
            var latestCustomerMessage = messageSnapshot?.LatestMessage ?? string.Empty;
            var filteredConversationContext = messageSnapshot?.ConversationContext ?? string.Empty;
            ocrTextForSample = filteredConversationContext;

            callbacks.SetOcrText(latestCustomerMessage);
            if (string.IsNullOrWhiteSpace(latestCustomerMessage))
            {
                if (latestEffectiveMessage?.SenderType == ChatMessageSenderType.Self)
                {
                    throw new RpaFlowException(
                        "最新有效消息为我方消息，不需要自动回复。",
                        "LatestMessageFromSelf",
                        "Info",
                        "Skipped",
                        latestEffectiveMessage.Text);
                }

                if (latestEffectiveMessage?.SenderType == ChatMessageSenderType.Unknown)
                {
                    throw new RpaFlowException(
                        "最新有效消息发送方无法可靠判断，转人工确认。",
                        "LatestMessageSenderUnknown",
                        "Warning",
                        "Skipped",
                        latestEffectiveMessage.Text);
                }

                throw new RpaFlowException("OCR 未识别到客户消息。", "OcrEmpty", "Warning", "Skipped");
            }

            var latestOcrConfidence = visualMessages.CustomerSnapshot is not null
                ? CalculateOcrConfidence(visualMessages.PendingCustomerMessageGroup?.Messages) ?? visualMessages.LatestEffectiveMessage?.OcrConfidence ?? visualMessages.OcrConfidence
                : latestMessageOcr?.OcrResult.Confidence ?? 0m;
            if (latestOcrConfidence < options.OcrMinConfidence)
            {
                var actionName = options.EnableVisionOcrReview
                    ? "VisionOcrReviewNotConfirmed"
                    : "OcrLowConfidence";
                throw new RpaFlowException(
                    $"OCR 置信度过低：{latestOcrConfidence:P0}，阈值：{options.OcrMinConfidence:P0}。",
                    actionName,
                    "Warning",
                    "Skipped",
                    latestCustomerMessage);
            }

            await backendClient.UpdateTaskResultAsync(
                task.Id,
                new UpdateRpaTaskResultRequest(
                    conversationKey,
                    customerDisplayName,
                    latestCustomerMessage,
                    null,
                    $"OCR:{latestOcrConfidence:0.0000}; Source:{visualMessages.Source}; Messages:{visualMessages.Messages.Count}; LatestSender:{visualMessages.LatestEffectiveMessage?.SenderType}; GroupMessages:{visualMessages.PendingCustomerMessageGroup?.Messages.Count ?? 0}; GroupRange:{visualMessages.PendingCustomerMessageGroup?.StartOrder}-{visualMessages.PendingCustomerMessageGroup?.EndOrder}; ContextLines:{CountOcrLines(filteredConversationContext)}; Fingerprint:{messageSnapshot?.Fingerprint}"),
                cancellationToken);

            callbacks.SetStatus("正在生成 AI 回复");
            var aiStopwatch = Stopwatch.StartNew();
            var suggestion = await backendClient.CreateReplySuggestionAsync(
                new CreateReplySuggestionRequest(
                    null,
                    task.Id,
                    latestCustomerMessage,
                    filteredConversationContext,
                    options.ProviderCode,
                    options.PromptTemplateCode,
                    options.MaxKnowledgeResults),
                cancellationToken);
            callbacks.AppendLog($"[性能] AI 回复接口完成，耗时 {aiStopwatch.ElapsedMilliseconds} ms。");

            callbacks.SetAiReply(suggestion.ReplyText);
            aiReplyTextForSample = suggestion.ReplyText;
            callbacks.SetRisk($"{suggestion.RiskLevel} / {suggestion.Status}");
            await backendClient.UpdateTaskResultAsync(
                task.Id,
                new UpdateRpaTaskResultRequest(
                    null,
                    null,
                    null,
                    suggestion.ReplyText,
                    $"{suggestion.RiskLevel}; AutoSend={suggestion.ShouldAutoSend}; Status={suggestion.Status}; {sendMode.ToStatusTag()}; AiElapsedMs={aiStopwatch.ElapsedMilliseconds}; {suggestion.FailureReason}"),
                cancellationToken);

            if (!suggestion.ShouldAutoSend || !string.Equals(suggestion.RiskLevel, "Low", StringComparison.OrdinalIgnoreCase))
            {
                throw new RpaFlowException(
                    suggestion.FailureReason ?? "AI 回复建议不允许自动发送。",
                    "AiManualReviewRequired",
                    "Warning",
                    "Skipped",
                    latestCustomerMessage,
                    suggestion.ReplyText,
                    suggestion.RiskLevel);
            }

            if (string.IsNullOrWhiteSpace(suggestion.ReplyText))
            {
                throw new RpaFlowException("AI 回复内容为空。", "AiReplyEmpty", "Warning", "Skipped", latestCustomerMessage);
            }

            if (!sendMode.ShouldInputReply())
            {
                callbacks.SetStatus("发送模式 DryRun，跳过输入和发送");
                callbacks.SetCountdown("DryRun：未输入");
                await SafeLogAsync(
                    task.Id,
                    "ReplyDryRunSkipped",
                    $"{sendMode.ToStatusTag()}，仅生成 AI 回复，不输入微信输入框、不点击发送按钮。",
                    latestCustomerMessage,
                    suggestion.ReplyText,
                    suggestion.RiskLevel,
                    "Warning",
                    cancellationToken);
            }
            else
            {
                await InputReplyAndVerifyAsync(
                    task.Id,
                    window!,
                    layout,
                    latestCustomerMessage,
                    suggestion.ReplyText,
                    suggestion.RiskLevel,
                    sendMode,
                    inputOnlyAfterVerifyAction,
                    windowLock,
                    cancellationToken);
 

                if (sendMode.ShouldClickSend())
                {
                    ValidateProductionSendModeOrThrow(sendMode, windowLock);
                    callbacks.SetStatus("发送前审核倒计时");
                    await ReviewCountdownAsync(cancellationToken);
                    ValidateWindowLockOrThrow(windowLock, "发送前");

                    callbacks.SetStatus("正在发送");
                    await inputExecutor.ClickAsync(layout.SendButtonPoint, options.ClickWaitMs, options, cancellationToken);
                    await SafeLogAsync(task.Id, "SendButtonClicked", $"已点击发送按钮，正在确认微信输入框是否清空，{sendMode.ToStatusTag()}。", latestCustomerMessage, suggestion.ReplyText, suggestion.RiskLevel, "Info", cancellationToken);
                    await VerifySendCompletedAsync(task.Id, layout, latestCustomerMessage, suggestion.ReplyText, suggestion.RiskLevel, cancellationToken);
                    await SafeLogAsync(task.Id, "ReplySent", $"发送后校验通过，微信输入框已清空，{sendMode.ToStatusTag()}。", latestCustomerMessage, suggestion.ReplyText, suggestion.RiskLevel, "Info", cancellationToken);
                }
                else
                {
                    callbacks.SetStatus($"发送模式 InputOnly，{inputOnlyAfterVerifyAction.ToDisplayText()}");
                    callbacks.SetCountdown("InputOnly：未发送");
                    await SafeLogAsync(
                        task.Id,
                        "ReplySendSkipped",
                        $"{sendMode.ToStatusTag()}，已输入微信输入框并通过 OCR 校验，跳过点击发送按钮；后处理={inputOnlyAfterVerifyAction}。",
                        latestCustomerMessage,
                        suggestion.ReplyText,
                        suggestion.RiskLevel,
                        "Warning",
                        cancellationToken);
                    await HandleInputOnlyAfterVerifyAsync(
                        task.Id,
                        layout,
                        latestCustomerMessage,
                        suggestion.ReplyText,
                        suggestion.RiskLevel,
                        inputOnlyAfterVerifyAction,
                        cancellationToken);
                }
            }

            callbacks.SetCountdown($"发送后等待 {options.MinSendIntervalSeconds} 秒");
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, options.MinSendIntervalSeconds)), cancellationToken);
            await backendClient.UpdateTaskStatusAsync(task.Id, "Succeeded", null, cancellationToken);
            finalTaskStatus = "Succeeded";
            callbacks.SetStatus("任务已完成");
            callbacks.AppendLog("单会话自动回复闭环完成。");
        }
        catch (OperationCanceledException)
        {
            finalTaskStatus = "Cancelled";
            finalFailureAction = "TaskCancelled";
            finalFailureReason = "员工已暂停或紧急停止。";
            callbacks.SetStatus("任务已取消");
            callbacks.SetCountdown("已取消");
            callbacks.SetError("员工已暂停或紧急停止。");
            if (task is not null)
            {
                await SafeLogAsync(task.Id, "TaskCancelled", "员工已暂停或紧急停止。", null, null, null, "Warning", CancellationToken.None);
                await SafeUpdateStatusAsync(task.Id, "Cancelled", "员工已暂停或紧急停止。", CancellationToken.None);
            }
        }
        catch (RpaFlowException ex)
        {
            finalTaskStatus = ex.TargetTaskStatus;
            finalFailureAction = ex.ActionName;
            finalFailureReason = ex.Message;
            callbacks.SetStatus(ex.TargetTaskStatus == "Skipped" ? "转人工处理" : "任务异常停止");
            callbacks.SetError(ex.Message);
            if (ex.TargetTaskStatus == "Skipped" && !IsManualReviewAction(ex.ActionName))
            {
                callbacks.SetStatus("Task skipped");
            }
            if (task is not null)
            {
                await SafeLogAsync(task.Id, ex.ActionName, ex.Message, ex.OcrText, ex.AiReplyText, ex.RiskResult, ex.LogLevel, CancellationToken.None);
                await SafeUpdateStatusAsync(task.Id, ex.TargetTaskStatus, ex.Message, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            finalTaskStatus = "Failed";
            finalFailureAction = "UnhandledException";
            finalFailureReason = ex.Message;
            callbacks.SetStatus("任务异常停止");
            callbacks.SetError(ex.Message);
            if (task is not null)
            {
                await SafeLogAsync(task.Id, "UnhandledException", ex.Message, null, null, null, "Error", CancellationToken.None);
                await SafeUpdateStatusAsync(task.Id, "Failed", ex.Message, CancellationToken.None);
            }
        }
        finally
        {
            await TryCaptureLearningSampleAsync(
                task?.Id,
                conversationKey,
                window,
                layout,
                yoloValidation,
                finalTaskStatus,
                finalFailureAction,
                finalFailureReason,
                ocrTextForSample,
                aiReplyTextForSample,
                CancellationToken.None);
        }

        return new SingleConversationReplyCycleResult(
            task?.Id,
            finalTaskStatus,
            finalFailureAction,
            finalFailureReason,
            messageSnapshot,
            aiReplyTextForSample);
    }

    private async Task InputReplyAndVerifyAsync(
        Guid taskId,
        WeChatWindow window,
        WeChatLayoutResult layout,
        string incomingText,
        string replyText,
        string riskLevel,
        RpaSendMode sendMode,
        RpaInputOnlyAfterVerifyAction inputOnlyAfterVerifyAction,
        WeChatWindowLock? windowLock,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, options.InputVerifyRetryCount + 1);
        string lastRecognizedText = string.Empty;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var isRetry = attempt > 1;
            callbacks.SetStatus(isRetry ? $"正在重试输入回复 {attempt}/{maxAttempts}" : "正在输入回复");
            windowLocator.Activate(window);
            await Task.Delay(Math.Max(120, options.ClickWaitMs), cancellationToken);
            ValidateWindowLockOrThrow(windowLock, isRetry ? $"输入前重试 {attempt}" : "输入前");

            await inputExecutor.ClickAsync(layout.InputClickPoint, options.ClickWaitMs, options, cancellationToken);
            await SafeLogAsync(
                taskId,
                isRetry ? "InputBoxClickedRetry" : "InputBoxClicked",
                $"已点击微信输入框，{sendMode.ToStatusTag()}，输入尝试 {attempt}/{maxAttempts}。",
                incomingText,
                replyText,
                riskLevel,
                "Info",
                cancellationToken);

            if (isRetry)
            {
                await SafeLogAsync(taskId, "InputRetryClearStarted", $"输入校验失败后开始清空并重试，尝试 {attempt}/{maxAttempts}。", incomingText, replyText, riskLevel, "Warning", cancellationToken);
                await inputExecutor.ClearTextAsync(options.ClickWaitMs, options, cancellationToken);
            }

            await inputExecutor.TypeTextAsync(replyText, options.KeyboardWaitMs, options, cancellationToken);
            await SafeLogAsync(
                taskId,
                isRetry ? "ReplyTypedRetry" : "ReplyTyped",
                $"AI 回复已输入微信输入框，{sendMode.ToStatusTag()}，输入尝试 {attempt}/{maxAttempts}。",
                incomingText,
                replyText,
                riskLevel,
                "Info",
                cancellationToken);

            await Task.Delay(Math.Max(0, options.InputVerifyDelayMs), cancellationToken);
            var verifyText = await CaptureAndRecognizeAsync(
                layout.InputVerifyRegion,
                isRetry ? "InputVerifyOcrRetry" : "InputVerifyOcr",
                taskId,
                cancellationToken);
            lastRecognizedText = verifyText.Text;

            if (InputVerifier.LooksLikeReply(verifyText.Text, replyText))
            {
                return;
            }

            if (attempt < maxAttempts)
            {
                await SafeLogAsync(
                    taskId,
                    "InputVerifyRetryScheduled",
                    $"输入框 OCR 校验未命中回复，准备重试。尝试 {attempt}/{maxAttempts}，OCR={FormatOcrPreview(verifyText.Text)}。",
                    incomingText,
                    replyText,
                    riskLevel,
                    "Warning",
                    cancellationToken);
            }
        }

        await TryClearInputOnlyDraftAfterVerifyFailureAsync(
            taskId,
            layout,
            incomingText,
            replyText,
            riskLevel,
            sendMode,
            inputOnlyAfterVerifyAction,
            cancellationToken);

        throw new RpaFlowException(
            $"输入框内容校验失败，不执行发送。输入框 OCR 识别到：{FormatOcrPreview(lastRecognizedText)}",
            "InputVerifyFailed",
            "Error",
            "Failed",
            incomingText,
            replyText,
            riskLevel);
    }

    private async Task HandleInputOnlyAfterVerifyAsync(
        Guid taskId,
        WeChatLayoutResult layout,
        string incomingText,
        string replyText,
        string riskLevel,
        RpaInputOnlyAfterVerifyAction action,
        CancellationToken cancellationToken)
    {
        switch (action)
        {
            case RpaInputOnlyAfterVerifyAction.KeepDraft:
                callbacks.SetCountdown("InputOnly：草稿已保留");
                await SafeLogAsync(
                    taskId,
                    "InputOnlyDraftKept",
                    "InputOnly 后处理为 KeepDraft，AI 回复保留在微信输入框，需人工确认后清理或发送。",
                    incomingText,
                    replyText,
                    riskLevel,
                    "Warning",
                    cancellationToken);
                return;

            case RpaInputOnlyAfterVerifyAction.SelectAllOnly:
                callbacks.SetCountdown("InputOnly：草稿已全选");
                await SafeLogAsync(taskId, "InputOnlySelectAllStarted", "开始全选 InputOnly 草稿，不点击发送按钮。", incomingText, replyText, riskLevel, "Info", cancellationToken);
                await inputExecutor.ClickAsync(layout.InputClickPoint, options.ClickWaitMs, options, cancellationToken);
                await inputExecutor.SelectAllAsync(options.ClickWaitMs, options, cancellationToken);
                await SafeLogAsync(taskId, "InputOnlyDraftSelected", "InputOnly 草稿已全选，未删除、未发送。", incomingText, replyText, riskLevel, "Warning", cancellationToken);
                return;

            case RpaInputOnlyAfterVerifyAction.ClearInput:
                callbacks.SetCountdown("InputOnly：正在清空输入框");
                await SafeLogAsync(taskId, "InputOnlyClearStarted", "开始清空 InputOnly 草稿，不点击发送按钮。", incomingText, replyText, riskLevel, "Info", cancellationToken);
                await inputExecutor.ClickAsync(layout.InputClickPoint, options.ClickWaitMs, options, cancellationToken);
                await inputExecutor.ClearTextAsync(options.ClickWaitMs, options, cancellationToken);
                await VerifyInputOnlyClearCompletedAsync(taskId, layout, incomingText, replyText, riskLevel, cancellationToken);
                callbacks.SetCountdown("InputOnly：已清空");
                await SafeLogAsync(taskId, "InputOnlyCleared", "InputOnly 草稿已清空并通过 OCR 复核，未点击发送按钮。", incomingText, replyText, riskLevel, "Info", cancellationToken);
                return;

            default:
                throw new RpaFlowException(
                    $"不支持的 InputOnly 后处理策略：{action}。",
                    "InputOnlyAfterVerifyActionUnsupported",
                    "Error",
                    "Failed",
                    incomingText,
                    replyText,
                    riskLevel);
        }
    }

    private async Task TryClearInputOnlyDraftAfterVerifyFailureAsync(
        Guid taskId,
        WeChatLayoutResult layout,
        string incomingText,
        string replyText,
        string riskLevel,
        RpaSendMode sendMode,
        RpaInputOnlyAfterVerifyAction action,
        CancellationToken cancellationToken)
    {
        if (sendMode != RpaSendMode.InputOnly || action != RpaInputOnlyAfterVerifyAction.ClearInput)
        {
            return;
        }

        try
        {
            callbacks.SetCountdown("InputOnly：校验失败后清空输入框");
            await SafeLogAsync(taskId, "InputOnlyClearAfterVerifyFailureStarted", "InputOnly 输入校验失败，尝试清空可能残留的草稿。", incomingText, replyText, riskLevel, "Warning", cancellationToken);
            await inputExecutor.ClickAsync(layout.InputClickPoint, options.ClickWaitMs, options, cancellationToken);
            await inputExecutor.ClearTextAsync(options.ClickWaitMs, options, cancellationToken);
            await VerifyInputOnlyClearCompletedAsync(taskId, layout, incomingText, replyText, riskLevel, cancellationToken);
            await SafeLogAsync(taskId, "InputOnlyClearedAfterVerifyFailure", "InputOnly 输入校验失败后已清空输入框并通过 OCR 复核。", incomingText, replyText, riskLevel, "Info", cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await SafeLogAsync(taskId, "InputOnlyClearAfterVerifyFailureFailed", $"InputOnly 输入校验失败后的清空兜底也失败：{ex.Message}", incomingText, replyText, riskLevel, "Error", CancellationToken.None);
        }
    }

    private async Task VerifyInputOnlyClearCompletedAsync(
        Guid taskId,
        WeChatLayoutResult layout,
        string incomingText,
        string replyText,
        string riskLevel,
        CancellationToken cancellationToken)
    {
        await Task.Delay(500, cancellationToken);
        var verifyText = await CaptureAndRecognizeAsync(
            layout.InputVerifyRegion,
            "InputOnlyClearVerifyOcr",
            taskId,
            cancellationToken);

        if (InputVerifier.LooksLikeReply(verifyText.Text, replyText) ||
            InputVerifier.HasUnexpectedInputText(verifyText.Text))
        {
            throw new RpaFlowException(
                $"InputOnly 清空后校验失败：输入框仍识别到非空文本。OCR：{FormatOcrPreview(verifyText.Text)}",
                "InputOnlyClearVerifyFailed",
                "Error",
                "Failed",
                incomingText,
                replyText,
                riskLevel);
        }
    }

    private void ValidateProductionSendModeOrThrow(RpaSendMode sendMode, WeChatWindowLock? windowLock)
    {
        if (!sendMode.RequiresProductionGuard())
        {
            return;
        }

        if (!options.EnableWindowTargetLock || windowLock is null)
        {
            throw new RpaFlowException(
                "ProductionGuarded 发送模式要求启用微信窗口锁定。",
                "SendModeGuardFailed",
                "Error",
                "Failed");
        }

        if (options.ReviewDelaySeconds < 3)
        {
            throw new RpaFlowException(
                "ProductionGuarded 发送模式要求发送前审核倒计时至少 3 秒。",
                "SendModeGuardFailed",
                "Error",
                "Failed");
        }
    }

    private void ValidateWindowLockOrThrow(WeChatWindowLock? windowLock, string phase)
    {
        if (!options.EnableWindowTargetLock || windowLock is null)
        {
            return;
        }

        var validation = windowLock.Validate(
            windowLocator.FindByHandle(windowLock.Handle),
            options.WindowTargetLockClientBoundsTolerancePixels);
        if (!validation.IsValid)
        {
            callbacks.SetWindowTargetText($"锁定失效：{windowLock.ToDisplayText()}");
            throw new RpaFlowException(
                $"微信窗口锁定校验失败（{phase}）：{validation.Reason}",
                "WindowLockValidationFailed",
                "Error",
                "Failed");
        }

        callbacks.SetWindowTargetText(windowLock.ToDisplayText());
        callbacks.AppendLog($"微信窗口锁定校验通过（{phase}）：{windowLock.ToDisplayText()}。");
    }

    private async Task TryCaptureLearningSampleAsync(
        Guid? taskId,
        string conversationKey,
        WeChatWindow? window,
        WeChatLayoutResult? layout,
        YoloLayoutValidationResult? yoloValidation,
        string taskStatus,
        string? failureAction,
        string? failureReason,
        string? ocrText,
        string? aiReplyText,
        CancellationToken cancellationToken)
    {
        if (window is null)
        {
            return;
        }

        try
        {
            var result = await _learningSampleCollector.CaptureAsync(
                window,
                layout,
                yoloValidation,
                options,
                taskId,
                conversationKey,
                taskStatus,
                failureAction,
                failureReason,
                ocrText,
                aiReplyText,
                cancellationToken);

            if (result is not null)
            {
                callbacks.AppendLog($"主动学习样本已保存：{result.Bucket}，草稿标签 {result.DraftLabelCount} 个，{result.ImagePath}");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            callbacks.AppendLog($"主动学习样本保存失败：{ex.Message}");
        }
    }

    private async Task<ChatMessageVisualExtractionResult> CaptureChatMessageFlowAsync(
        Rectangle conversationContextRegion,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var result = await _chatMessageVisualExtractor.ExtractAsync(
            conversationContextRegion,
            options,
            taskId,
            cancellationToken,
            extractionOptions: new ChatMessageVisualExtractionOptions(
                0,
                1m,
                false,
                options.EnablePerformanceDiagnostics ? callbacks.AppendLog : null,
                callbacks.SetStatus));
        callbacks.SetContinuousLatestSenderText(result.LatestEffectiveMessage?.SenderDisplayName ?? "无");
        callbacks.SetContinuousLatestMessageText(result.PendingCustomerMessageGroup?.QuestionText ?? result.LatestEffectiveMessage?.Text ?? "未识别到有效消息");
        callbacks.AppendLog($"视觉消息流解析完成：{result.Summary}");

        await SafeLogAsync(
            taskId,
            "ChatMessageFlowExtracted",
            string.IsNullOrWhiteSpace(result.DebugCapturePath)
                ? result.Summary
                : $"{result.Summary} 调试截图：{result.DebugCapturePath}",
            ChatMessageFlowAnalyzer.FormatConversationContext(result.Messages),
            null,
            $"Messages:{result.Messages.Count}; LatestSender:{result.LatestEffectiveMessage?.SenderType}; GroupMessages:{result.PendingCustomerMessageGroup?.Messages.Count ?? 0}; OCR:{result.OcrConfidence:0.0000}",
            "Info",
            cancellationToken);

        return result;
    }

    private async Task<LatestCustomerMessageOcrResult> CaptureLegacyLatestCustomerMessageAsync(
        WeChatLayoutResult layout,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var conversationContextText = await CaptureAndRecognizeAsync(
            layout.ConversationContextRegion,
            "ConversationContextOcr",
            taskId,
            cancellationToken);
        var aiConversationContext = string.IsNullOrWhiteSpace(conversationContextText.Text)
            ? string.Empty
            : conversationContextText.Text;

        callbacks.AppendLog("视觉消息流未识别到有效消息，回退左侧客户消息 OCR。");
        return await CaptureLatestCustomerMessageAsync(
            layout.IncomingMessageRegion,
            aiConversationContext,
            taskId,
            cancellationToken);
    }

    private async Task<OcrResult> CaptureAndRecognizeAsync(
        Rectangle region,
        string actionName,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var captureStopwatch = Stopwatch.StartNew();
        using var capture = screenCaptureService.Capture(region);
        callbacks.AppendLog($"[性能] {actionName} 截图完成，耗时 {captureStopwatch.ElapsedMilliseconds} ms。");

        var debugCapturePath = TrySaveDebugCapture(capture, actionName, taskId);
        var ocrStopwatch = Stopwatch.StartNew();
        var result = await ocrEngine.RecognizeAsync(capture.PngBytes, cancellationToken);
        var message = $"OCR 完成，来源 {result.Source}，置信度 {result.Confidence:0.0000}，截图区域 {region}，OCR耗时 {ocrStopwatch.ElapsedMilliseconds} ms。";
        if (!string.IsNullOrWhiteSpace(debugCapturePath))
        {
            message += $" 调试截图：{debugCapturePath}";
        }

        await SafeLogAsync(
            taskId,
            actionName,
            message,
            result.Text,
            null,
            $"OCR:{result.Confidence:0.0000}; OcrElapsedMs:{ocrStopwatch.ElapsedMilliseconds}",
            "Info",
            cancellationToken);
        return result;
    }

    private async Task<LatestCustomerMessageOcrResult> CaptureLatestCustomerMessageAsync(
        Rectangle fullIncomingRegion,
        string? conversationContext,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        LatestCustomerMessageOcrResult? fallback = null;
        var scanRegions = CustomerMessageOcrScanPlanner.CreateBottomUpScanRegions(fullIncomingRegion);
        for (var index = 0; index < scanRegions.Count; index++)
        {
            var region = scanRegions[index];
            var isFullRegion = region.Equals(fullIncomingRegion);
            var result = await CaptureAndRecognizeAsync(
                region,
                isFullRegion ? "IncomingMessageOcr" : $"IncomingMessageBottomSliceOcr{index + 1}",
                taskId,
                cancellationToken);
            var snapshot = CustomerMessageExtractor.ExtractSnapshot(result.Text, conversationContext);
            var attempt = new LatestCustomerMessageOcrResult(snapshot, result, region, !isFullRegion);

            if (snapshot is not null && snapshot.HasMessage)
            {
                await SafeLogAsync(
                    taskId,
                    "LatestCustomerMessageSelected",
                    $"已选择最新客户消息：{snapshot.LatestMessage}，来源区域 {region}，底部切片={attempt.UsedBottomUpSlice}。",
                    result.Text,
                    null,
                    $"OCR:{result.Confidence:0.0000}",
                    "Info",
                    cancellationToken);
                return attempt;
            }

            fallback ??= attempt;
        }

        return fallback ?? new LatestCustomerMessageOcrResult(
            null,
            new OcrResult(string.Empty, 0, "NoScanRegion"),
            fullIncomingRegion,
            false);
    }

    private static int CountOcrLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return text
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries)
            .Count(line => !string.IsNullOrWhiteSpace(line));
    }

    private static decimal? CalculateOcrConfidence(IReadOnlyList<ChatMessageItem>? messages)
    {
        var values = messages?
            .Select(message => message.OcrConfidence)
            .Where(confidence => confidence > 0)
            .ToArray();

        return values is { Length: > 0 } ? values.Average() : null;
    }

    private static bool IsManualReviewAction(string actionName)
    {
        return string.Equals(actionName, "AiManualReviewRequired", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatOcrPreview(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "空";
        }

        var compact = Regex.Replace(text.Trim(), @"\s+", " ");
        return compact.Length <= 80 ? compact : $"{compact[..80]}...";
    }

    private async Task<YoloLayoutValidationResult> RunYoloLayoutValidationAsync(
        WeChatWindow window,
        WeChatLayoutResult layout,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var yoloStopwatch = Stopwatch.StartNew();
        var result = await yoloVisionDetector.ValidateAsync(window, layout, options, taskId, cancellationToken);
        callbacks.AppendLog($"[性能] YOLO 旁路验证完成，耗时 {yoloStopwatch.ElapsedMilliseconds} ms。");
        callbacks.SetLayoutStatus($"{layout.Mode} / {layout.Confidence:P0} / {result.Status}");

        var actionName = result.Skipped
            ? "YoloLayoutValidationSkipped"
            : result.Succeeded
                ? "YoloLayoutValidationSucceeded"
                : "YoloLayoutValidationFailed";
        var level = result.Skipped
            ? "Info"
            : result.Succeeded
                ? "Info"
                : "Warning";

        await SafeLogAsync(
            taskId,
            actionName,
            result.ToLogMessage(),
            null,
            null,
            $"YoloDetections:{result.DetectionCount}; ElapsedMs:{yoloStopwatch.ElapsedMilliseconds}",
            level,
            cancellationToken);

        return result;
    }

    private async Task VerifySendCompletedAsync(
        Guid taskId,
        WeChatLayoutResult layout,
        string incomingText,
        string replyText,
        string riskLevel,
        CancellationToken cancellationToken)
    {
        await Task.Delay(800, cancellationToken);
        var afterClickVerifyText = await CaptureAndRecognizeAsync(
            layout.InputVerifyRegion,
            "PostSendInputVerifyOcr",
            taskId,
            cancellationToken);

        if (!InputVerifier.LooksLikeReply(afterClickVerifyText.Text, replyText))
        {
            if (InputVerifier.HasUnexpectedInputText(afterClickVerifyText.Text))
            {
                throw new RpaFlowException(
                    $"发送后校验异常：输入校验区仍识别到非空文本，可能覆盖了聊天气泡或输入框未清空。OCR：{FormatOcrPreview(afterClickVerifyText.Text)}",
                    "SendVerifyLayoutSuspicious",
                    "Error",
                    "Failed",
                    incomingText,
                    replyText,
                    riskLevel);
            }

            return;
        }

        await SafeLogAsync(
            taskId,
            "SendStillPendingAfterClick",
            "点击发送后输入框仍保留回复内容，尝试使用 Enter 兜底发送。",
            incomingText,
            replyText,
            riskLevel,
            "Warning",
            cancellationToken);

        await inputExecutor.ClickAsync(layout.InputClickPoint, options.ClickWaitMs, options, cancellationToken);
        await inputExecutor.PressEnterAsync(options.ClickWaitMs, options, cancellationToken);
        await Task.Delay(800, cancellationToken);

        var afterEnterVerifyText = await CaptureAndRecognizeAsync(
            layout.InputVerifyRegion,
            "PostEnterSendInputVerifyOcr",
            taskId,
            cancellationToken);

        if (InputVerifier.LooksLikeReply(afterEnterVerifyText.Text, replyText))
        {
            throw new RpaFlowException(
                "发送后校验失败：输入框仍保留 AI 回复内容，未确认真实发送成功。",
                "SendVerifyFailed",
                "Error",
                "Failed",
                incomingText,
                replyText,
                riskLevel);
        }

        if (InputVerifier.HasUnexpectedInputText(afterEnterVerifyText.Text))
        {
            throw new RpaFlowException(
                $"发送后校验异常：Enter 兜底后输入校验区仍识别到非空文本，可能覆盖了聊天气泡或输入框未清空。OCR：{FormatOcrPreview(afterEnterVerifyText.Text)}",
                "SendVerifyLayoutSuspicious",
                "Error",
                "Failed",
                incomingText,
                replyText,
                riskLevel);
        }
    }

    private string? TrySaveDebugCapture(CapturedImage capture, string actionName, Guid taskId)
    {
        if (!options.EnableDebugCaptures)
        {
            return null;
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var directory = string.IsNullOrWhiteSpace(options.DebugCaptureDirectory)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AIChat",
                    "RpaClient",
                    "debug-captures")
                : Environment.ExpandEnvironmentVariables(options.DebugCaptureDirectory);

            Directory.CreateDirectory(directory);
            var safeActionName = Regex.Replace(actionName, @"[^\w.-]+", "_");
            var fileName = $"{DateTime.Now:yyyyMMdd-HHmmssfff}-{taskId:N}-{safeActionName}-{capture.Bounds.X}-{capture.Bounds.Y}-{capture.Bounds.Width}x{capture.Bounds.Height}.png";
            var path = Path.Combine(directory, fileName);
            File.WriteAllBytes(path, capture.PngBytes);
            callbacks.AppendLog($"[性能] OCR 调试截图保存完成，耗时 {stopwatch.ElapsedMilliseconds} ms。");
            return path;
        }
        catch (Exception ex)
        {
            callbacks.AppendLog($"OCR 调试截图保存失败：{ex.Message}");
            return null;
        }
    }

    private async Task ReviewCountdownAsync(CancellationToken cancellationToken)
    {
        var seconds = Math.Max(0, options.ReviewDelaySeconds);
        for (var remaining = seconds; remaining > 0; remaining--)
        {
            callbacks.SetCountdown($"{remaining} 秒后发送，可暂停或紧急停止");
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        callbacks.SetCountdown("审核倒计时结束");
    }

    private async Task SafeLogAsync(
        Guid taskId,
        string actionName,
        string message,
        string? ocrText,
        string? aiReplyText,
        string? riskResult,
        string level,
        CancellationToken cancellationToken)
    {
        callbacks.AppendLog(message);
        try
        {
            await backendClient.AddActionLogAsync(
                taskId,
                new CreateRpaActionLogRequest(level, actionName, message, ocrText, aiReplyText, riskResult, null),
                cancellationToken);
        }
        catch (Exception ex)
        {
            callbacks.AppendLog($"动作日志上报失败：{ex.Message}");
        }
    }

    private async Task SafeUpdateStatusAsync(Guid taskId, string status, string? errorMessage, CancellationToken cancellationToken)
    {
        try
        {
            await backendClient.UpdateTaskStatusAsync(taskId, status, errorMessage, cancellationToken);
        }
        catch (Exception ex)
        {
            callbacks.AppendLog($"任务状态回写失败：{ex.Message}");
        }
    }
}

public sealed class SingleConversationTaskRunner(
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
    private readonly SingleConversationReplyCycleExecutor _executor = new(
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

    public Task<SingleConversationReplyCycleResult> RunAsync(
        Guid clientInstanceId,
        Guid? employeeId,
        Guid? weChatWorkAccountId,
        CancellationToken cancellationToken)
    {
        return _executor.RunAsync(clientInstanceId, employeeId, weChatWorkAccountId, cancellationToken);
    }
}

public sealed record SingleConversationReplyCycleRequest(
    Guid ClientInstanceId,
    Guid? EmployeeId,
    Guid? WeChatWorkAccountId,
    string? ConversationKey,
    string? CustomerDisplayName,
    bool IsContinuous,
    WeChatWindow? Window = null,
    WeChatLayoutResult? Layout = null,
    ChatMessageVisualExtractionResult? VisualMessages = null,
    WeChatWindowLock? WindowLock = null);

public sealed record SingleConversationReplyCycleResult(
    Guid? TaskId,
    string TaskStatus,
    string? FailureAction,
    string? FailureReason,
    CustomerMessageSnapshot? Snapshot,
    string? AiReplyText)
{
    public bool Succeeded => string.Equals(TaskStatus, "Succeeded", StringComparison.OrdinalIgnoreCase);

    public bool RequiresManualReview =>
        string.Equals(FailureAction, "AiManualReviewRequired", StringComparison.OrdinalIgnoreCase);

    public bool IsSendFailure =>
        string.Equals(TaskStatus, "Failed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(FailureAction, "InputVerifyFailed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(FailureAction, "SendVerifyFailed", StringComparison.OrdinalIgnoreCase);
}

public sealed record RpaTaskRunnerCallbacks(
    Action<string> AppendLog,
    Action<string> SetStatus,
    Action<string> SetOcrText,
    Action<string> SetAiReply,
    Action<string> SetRisk,
    Action<string> SetLayoutStatus,
    Action<string> SetCountdown,
    Action<string> SetError,
    Action<string>? SetContinuousStatus = null,
    Action<string>? SetContinuousReplyCount = null,
    Action<string>? SetContinuousLastPoll = null,
    Action<string>? SetContinuousLatestMessage = null,
    Action<string>? SetContinuousLatestSender = null,
    Action<string>? SetContinuousMergeCountdown = null,
    Action<string>? SetContinuousFailureCount = null,
    Action<string>? SetWindowTarget = null,
    Action<UnreadConversationQueueSnapshot>? SetUnreadQueueSnapshot = null)
{
    public void SetContinuousStatusText(string value)
    {
        SetContinuousStatus?.Invoke(value);
    }

    public void SetContinuousReplyCountText(string value)
    {
        SetContinuousReplyCount?.Invoke(value);
    }

    public void SetContinuousLastPollText(string value)
    {
        SetContinuousLastPoll?.Invoke(value);
    }

    public void SetContinuousLatestMessageText(string value)
    {
        SetContinuousLatestMessage?.Invoke(value);
    }

    public void SetContinuousLatestSenderText(string value)
    {
        SetContinuousLatestSender?.Invoke(value);
    }

    public void SetContinuousMergeCountdownText(string value)
    {
        SetContinuousMergeCountdown?.Invoke(value);
    }

    public void SetContinuousFailureCountText(string value)
    {
        SetContinuousFailureCount?.Invoke(value);
    }

    public void SetWindowTargetText(string value)
    {
        SetWindowTarget?.Invoke(value);
    }

    public void SetUnreadQueueSnapshotValue(UnreadConversationQueueSnapshot snapshot)
    {
        SetUnreadQueueSnapshot?.Invoke(snapshot);
    }
}

public sealed class RpaFlowException(
    string message,
    string actionName,
    string logLevel,
    string targetTaskStatus,
    string? ocrText = null,
    string? aiReplyText = null,
    string? riskResult = null) : Exception(message)
{
    public string ActionName { get; } = actionName;
    public string LogLevel { get; } = logLevel;
    public string TargetTaskStatus { get; } = targetTaskStatus;
    public string? OcrText { get; } = ocrText;
    public string? AiReplyText { get; } = aiReplyText;
    public string? RiskResult { get; } = riskResult;
}

public static class InputVerifier
{
    public static bool LooksLikeReply(string recognizedInputText, string expectedReply)
    {
        var recognized = Normalize(recognizedInputText);
        var expected = Normalize(expectedReply);
        if (recognized.Length == 0 || expected.Length == 0)
        {
            return false;
        }

        var probeLength = Math.Min(6, expected.Length);
        if (recognized.Contains(expected[..probeLength], StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var commonLength = CalculateLongestCommonSubsequenceLength(recognized, expected);
        if (expected.Length <= 6)
        {
            var requiredCommonLength = Math.Max(2, expected.Length - 1);
            if (recognized.Length >= requiredCommonLength && commonLength >= requiredCommonLength)
            {
                return true;
            }
        }

        var similarity = commonLength / (double)expected.Length;
        var enoughVisibleText = recognized.Length >= Math.Max(4, expected.Length * 0.45d);
        return enoughVisibleText && similarity >= 0.72d;
    }

    public static bool HasUnexpectedInputText(string recognizedInputText)
    {
        var normalized = Normalize(recognizedInputText);
        if (normalized.Length < 2)
        {
            return false;
        }

        return !normalized.Equals("发送", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Equals("按住说话", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
    {
        var compact = Regex.Replace(value, @"\s+", string.Empty);
        return new string(compact.Where(IsComparableCharacter).ToArray());
    }

    private static bool IsComparableCharacter(char value)
    {
        return char.IsLetterOrDigit(value) || value is >= '\u4e00' and <= '\u9fff';
    }

    private static int CalculateLongestCommonSubsequenceLength(string first, string second)
    {
        var previous = new int[second.Length + 1];
        var current = new int[second.Length + 1];

        for (var i = 1; i <= first.Length; i++)
        {
            for (var j = 1; j <= second.Length; j++)
            {
                current[j] = first[i - 1] == second[j - 1]
                    ? previous[j - 1] + 1
                    : Math.Max(previous[j], current[j - 1]);
            }

            (previous, current) = (current, previous);
            Array.Clear(current);
        }

        return previous[second.Length];
    }
}
