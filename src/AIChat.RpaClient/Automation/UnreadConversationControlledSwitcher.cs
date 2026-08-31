using System.Drawing;
using System.IO;
using AIChat.RpaClient.Configuration;

namespace AIChat.RpaClient.Automation;

public sealed class UnreadConversationControlledSwitcher(
    MouseKeyboardExecutor inputExecutor,
    ScreenCaptureService screenCaptureService,
    PaddleOcrEngine ocrEngine,
    WeChatWindowLocator windowLocator)
{
    public async Task<UnreadConversationSwitchResult> SwitchAsync(
        WeChatWindow window,
        WeChatLayoutResult layout,
        UnreadConversationCandidate target,
        RpaAutomationOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = UnreadConversationSwitchPlanner.ValidateSwitchTarget(target, layout.ConversationListRegion);
        if (!validation.IsAllowed)
        {
            return CreateResult(false, "切换已阻止", validation.Reason, target, Point.Empty, Rectangle.Empty, string.Empty);
        }

        var windowLock = WeChatWindowLock.Capture(window, DateTimeOffset.UtcNow);
        var beforeClick = windowLock.Validate(
            windowLocator.FindByHandle(windowLock.Handle),
            options.WindowTargetLockClientBoundsTolerancePixels);
        if (!beforeClick.IsValid)
        {
            return CreateResult(false, "切换已阻止", $"点击前窗口锁校验失败：{beforeClick.Reason}", target, Point.Empty, Rectangle.Empty, string.Empty);
        }

        var clickPoint = UnreadConversationSwitchPlanner.CreateClickPoint(target.RowBounds);
        windowLocator.Activate(window);
        await Task.Delay(Math.Max(120, options.ClickWaitMs), cancellationToken);
        beforeClick = windowLock.Validate(
            windowLocator.FindByHandle(windowLock.Handle),
            options.WindowTargetLockClientBoundsTolerancePixels);
        if (!beforeClick.IsValid)
        {
            return CreateResult(false, "切换已阻止", $"点击前窗口锁复核失败：{beforeClick.Reason}", target, clickPoint, Rectangle.Empty, string.Empty);
        }

        await inputExecutor.ClickAsync(clickPoint, options.ClickWaitMs, options, cancellationToken);
        await Task.Delay(Math.Max(250, options.UnreadQueueSwitchPostClickVerifyDelayMs), cancellationToken);

        var afterClick = windowLock.Validate(
            windowLocator.FindByHandle(windowLock.Handle),
            options.WindowTargetLockClientBoundsTolerancePixels);
        if (!afterClick.IsValid)
        {
            return CreateResult(false, "切换后校验失败", afterClick.Reason, target, clickPoint, Rectangle.Empty, string.Empty);
        }

        var titleRegion = UnreadConversationSwitchPlanner.CreateTitleRegion(window.ClientBounds, layout.ChatContentRegion);
        var titleVerification = await RecognizeTitleAsync(titleRegion, options, cancellationToken);
        var titleOcr = titleVerification.Result;
        var titleVerified = UnreadConversationSwitchPlanner.TitleMatchesTarget(titleOcr.Text, target.TextInfo?.ConversationName);
        string titleReason;
        if (titleVerified)
        {
            titleReason = $"右侧聊天标题校验通过{FormatDebugPath(titleVerification.DebugCapturePath, "标题截图")}";
        }
        else
        {
            var rowVerification = await VerifySelectedRowAsync(target.RowBounds, options, cancellationToken);
            if (string.IsNullOrWhiteSpace(titleOcr.Text) && rowVerification.IsSelected)
            {
                titleReason = $"右侧标题 OCR 为空，候选行选中态校验通过{FormatDebugPath(titleVerification.DebugCapturePath, "标题截图")}{FormatDebugPath(rowVerification.DebugCapturePath, "行截图")}";
            }
            else
            {
                var reason = string.IsNullOrWhiteSpace(titleOcr.Text)
                    ? $"右侧聊天标题 OCR 为空，候选行选中态校验未通过{FormatDebugPath(titleVerification.DebugCapturePath, "标题截图")}{FormatDebugPath(rowVerification.DebugCapturePath, "行截图")}。"
                    : $"右侧聊天标题与目标会话名不一致{FormatDebugPath(titleVerification.DebugCapturePath, "标题截图")}。";
                return CreateResult(false, "切换后校验失败", reason, target, clickPoint, titleRegion, titleOcr.Text);
            }
        }

        var messageVerification = await VerifyMessageSummaryAsync(layout, target, options, cancellationToken);
        if (messageVerification.IsBlockingFailure)
        {
            return CreateResult(false, "切换后摘要校验失败", $"{titleReason}；{messageVerification.Status}：{messageVerification.Reason}。", target, clickPoint, titleRegion, titleOcr.Text, messageVerification);
        }

        return CreateResult(true, "切换成功", $"{titleReason}；{messageVerification.Status}，未输入、未发送。", target, clickPoint, titleRegion, titleOcr.Text, messageVerification);
    }

    private async Task<SwitchOcrVerification> RecognizeTitleAsync(Rectangle titleRegion, RpaAutomationOptions options, CancellationToken cancellationToken)
    {
        if (titleRegion.IsEmpty || titleRegion.Width <= 0 || titleRegion.Height <= 0)
        {
            return new SwitchOcrVerification(new OcrResult(string.Empty, 0m, "UnreadSwitchTitleRegionEmpty"), null);
        }

        using var capture = screenCaptureService.Capture(titleRegion);
        var debugPath = SaveSwitchCapture(capture, options, "Title");
        var result = await ocrEngine.RecognizeUiTextAsync(capture.PngBytes, cancellationToken);
        return new SwitchOcrVerification(result with { Source = $"UnreadSwitchTitleOcr:{result.Source}" }, debugPath);
    }

    private Task<SwitchSelectionVerification> VerifySelectedRowAsync(Rectangle rowBounds, RpaAutomationOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (rowBounds.IsEmpty || rowBounds.Width <= 0 || rowBounds.Height <= 0)
        {
            return Task.FromResult(new SwitchSelectionVerification(false, null));
        }

        using var capture = screenCaptureService.Capture(rowBounds);
        var debugPath = SaveSwitchCapture(capture, options, "SelectedRow");
        var isSelected = UnreadConversationSwitchPlanner.LooksSelectedConversationRow(capture.Bitmap.Width, capture.Bitmap.Height, (x, y) =>
        {
            var color = capture.Bitmap.GetPixel(x, y);
            return (color.R, color.G, color.B);
        });
        return Task.FromResult(new SwitchSelectionVerification(isSelected, debugPath));
    }

    private async Task<UnreadConversationMessageVerification> VerifyMessageSummaryAsync(
        WeChatLayoutResult layout,
        UnreadConversationCandidate target,
        RpaAutomationOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.EnableUnreadQueuePostSwitchMessageVerify)
        {
            return new UnreadConversationMessageVerification(
                false,
                true,
                "摘要校验跳过",
                "配置已关闭点击后摘要一致性校验",
                target.TextInfo?.LatestMessagePreview ?? string.Empty,
                string.Empty,
                "Disabled",
                null);
        }

        if (!UnreadConversationMessageVerifier.HasComparableQueuePreview(target, options.UnreadQueuePostSwitchMessageVerifyMinChars))
        {
            return new UnreadConversationMessageVerification(
                false,
                true,
                "摘要校验跳过",
                "队列摘要缺少可比较文本",
                target.TextInfo?.LatestMessagePreview ?? string.Empty,
                string.Empty,
                "UnreadSwitchMessagePreviewEmpty",
                null);
        }

        var region = UnreadConversationSwitchPlanner.CreateMessageVerifyRegion(
            layout.ConversationContextRegion.IsEmpty ? layout.ChatContentRegion : layout.ConversationContextRegion,
            options.ContinuousVisualOcrBottomRatio);
        if (region.IsEmpty || region.Width <= 0 || region.Height <= 0)
        {
            return new UnreadConversationMessageVerification(
                false,
                false,
                "摘要校验失败",
                "聊天消息区域无效",
                target.TextInfo?.LatestMessagePreview ?? string.Empty,
                string.Empty,
                "UnreadSwitchMessageRegionEmpty",
                null);
        }

        using var capture = screenCaptureService.Capture(region);
        var debugPath = SaveSwitchCapture(capture, options, "MessageVerify");
        var ocr = await ocrEngine.RecognizeUiTextAsync(capture.PngBytes, cancellationToken);
        var messages = CreateUiOcrMessages(ocr, region);
        var visualMessages = ChatMessageFlowAnalyzer.CreateResult(messages, $"UnreadSwitchMessageOcr:{ocr.Source}", debugPath);
        return UnreadConversationMessageVerifier.Verify(
            target,
            visualMessages,
            options.UnreadQueuePostSwitchMessageVerifyMinChars);
    }

    private static IReadOnlyList<ChatMessageItem> CreateUiOcrMessages(OcrResult ocr, Rectangle region)
    {
        if (string.IsNullOrWhiteSpace(ocr.Text))
        {
            return [];
        }

        return ocr.Text
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select((line, index) => new ChatMessageItem(
                ChatMessageSenderType.Unknown,
                line,
                CreateLineBounds(region, index),
                ocr.Confidence,
                index,
                $"UnreadSwitchMessageOcr:{ocr.Source}"))
            .ToArray();
    }

    private static Rectangle CreateLineBounds(Rectangle region, int index)
    {
        var lineHeight = Math.Max(1, region.Height / 24);
        var top = Math.Min(region.Bottom - lineHeight, region.Top + index * lineHeight);
        return new Rectangle(region.Left, top, region.Width, lineHeight);
    }

    private static string? SaveSwitchCapture(CapturedImage capture, RpaAutomationOptions options, string name)
    {
        if (!options.EnableUnreadQueueDebugCaptures)
        {
            return null;
        }

        var directory = string.IsNullOrWhiteSpace(options.UnreadQueueDebugCaptureDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIChat", "RpaClient", "unread-queue-captures")
            : Environment.ExpandEnvironmentVariables(options.UnreadQueueDebugCaptureDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}-UnreadSwitch{name}.png");
        File.WriteAllBytes(path, capture.PngBytes);
        return path;
    }

    private static string FormatDebugPath(string? path, string label)
    {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : $"，{label}：{path}";
    }

    private sealed record SwitchOcrVerification(OcrResult Result, string? DebugCapturePath);

    private sealed record SwitchSelectionVerification(bool IsSelected, string? DebugCapturePath);

    private static UnreadConversationSwitchResult CreateResult(
        bool isSuccess,
        string status,
        string reason,
        UnreadConversationCandidate target,
        Point clickPoint,
        Rectangle titleRegion,
        string titleOcrText,
        UnreadConversationMessageVerification? messageVerification = null)
    {
        return new UnreadConversationSwitchResult(isSuccess, status, reason, target, clickPoint, titleRegion, titleOcrText, messageVerification);
    }
}
