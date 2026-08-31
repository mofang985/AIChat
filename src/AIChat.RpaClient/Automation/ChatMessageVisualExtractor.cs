using System.Drawing;
using System.Diagnostics;
using System.IO;
using AIChat.RpaClient.Configuration;
using OpenCvSharp;
using DrawingRectangle = System.Drawing.Rectangle;
using CvPoint = OpenCvSharp.Point;
using CvRect = OpenCvSharp.Rect;
using CvSize = OpenCvSharp.Size;

namespace AIChat.RpaClient.Automation;

public sealed class ChatMessageVisualExtractor(
    ScreenCaptureService screenCaptureService,
    PaddleOcrEngine ocrEngine,
    VisionOcrReviewer visionOcrReviewer)
{
    public async Task<ChatMessageVisualExtractionResult> ExtractAsync(
        DrawingRectangle conversationContextRegion,
        RpaAutomationOptions options,
        Guid taskId,
        CancellationToken cancellationToken,
        bool saveDebugCapture = true,
        ChatMessageVisualExtractionOptions? extractionOptions = null)
    {
        var totalStopwatch = Stopwatch.StartNew();
        extractionOptions ??= ChatMessageVisualExtractionOptions.Default;
        var runtimeStats = new ChatMessageVisualExtractionRuntimeStats();

        var captureStopwatch = Stopwatch.StartNew();
        using var capture = screenCaptureService.Capture(conversationContextRegion);
        LogPerformance(extractionOptions, $"聊天区截图完成，区域 {conversationContextRegion}，耗时 {captureStopwatch.ElapsedMilliseconds} ms。");

        var decodeStopwatch = Stopwatch.StartNew();
        using var image = Cv2.ImDecode(capture.PngBytes, ImreadModes.Color);
        LogPerformance(extractionOptions, $"聊天区截图解码完成，尺寸 {image.Width}x{image.Height}，耗时 {decodeStopwatch.ElapsedMilliseconds} ms。");
        if (image.Empty())
        {
            return ChatMessageFlowAnalyzer.CreateResult(
                [],
                "VisualMessageFlow:EmptyImage");
        }

        var frameFingerprint = TryCreateFrameFingerprint(image, extractionOptions.UnchangedFrameBottomRatio);
        if (extractionOptions.EnableUnchangedFrameSkip &&
            extractionOptions.VisualCache is not null &&
            !string.IsNullOrWhiteSpace(frameFingerprint) &&
            extractionOptions.VisualCache.TryGetUnchangedFrame(
                conversationContextRegion,
                image.Width,
                image.Height,
                frameFingerprint,
                out var cachedFrameResult))
        {
            runtimeStats.FrameSkipCount++;
            extractionOptions.StageStatus?.Invoke("画面未变化，跳过识别");
            LogPerformance(extractionOptions, $"聊天区底部画面未变化，跳过气泡检测、OCR 和 VLM；总耗时 {totalStopwatch.ElapsedMilliseconds} ms。");
            return cachedFrameResult with
            {
                Source = AppendSource(cachedFrameResult.Source, "FrameCache")
            };
        }

        var detectStopwatch = Stopwatch.StartNew();
        var allCandidates = DetectCandidates(image);
        LogPerformance(extractionOptions, $"气泡候选检测完成，候选 {allCandidates.Count} 个，耗时 {detectStopwatch.ElapsedMilliseconds} ms。");

        var candidates = SelectCandidatesForOcr(allCandidates, image, extractionOptions);
        if (candidates.Count != allCandidates.Count)
        {
            LogPerformance(extractionOptions, $"连续监听候选裁剪：全部 {allCandidates.Count} 个，仅 OCR 底部最近 {candidates.Count} 个。");
        }

        var recognizedMessages = await RecognizeCandidatesAsync(
            image,
            capture.Bounds,
            candidates,
            options,
            taskId,
            allowVisionReview: !extractionOptions.ReviewOnlyPendingCustomerGroup,
            extractionOptions,
            runtimeStats,
            cancellationToken);

        if (extractionOptions.ReviewOnlyPendingCustomerGroup)
        {
            var reviewIndexes = FindContinuousVisionReviewCandidateIndexes(candidates, recognizedMessages, options);
            if (reviewIndexes.Count > 0)
            {
                LogPerformance(extractionOptions, $"VLM 复核范围限定为连续监听关键候选 {reviewIndexes.Count} 个。");
                foreach (var index in reviewIndexes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var existing = recognizedMessages.FirstOrDefault(item => item.CandidateIndex == index);
                    var forceVisionReview = IsAllRecognizedMessagesVisionReviewScope(options.ContinuousVisionReviewScope) ||
                        options.ContinuousReviewLatestSelfMessage &&
                        IsLatestSelfCandidate(index, candidates);
                    var reviewed = existing is not null
                        ? await ReviewRecognizedCandidateAsync(
                            existing,
                            options,
                            taskId,
                            extractionOptions,
                            runtimeStats,
                            forceVisionReview,
                            cancellationToken)
                        : await RecognizeCandidateAsync(
                            image,
                            capture.Bounds,
                            candidates[index],
                            options,
                            taskId,
                            index,
                            allowVisionReview: true,
                            forceVisionReview,
                            extractionOptions,
                            runtimeStats,
                            cancellationToken);
                    if (reviewed is null)
                    {
                        recognizedMessages.RemoveAll(item => item.CandidateIndex == index);
                        continue;
                    }

                    var existingIndex = recognizedMessages.FindIndex(item => item.CandidateIndex == index);
                    if (existingIndex >= 0)
                    {
                        recognizedMessages[existingIndex] = reviewed;
                    }
                    else
                    {
                        recognizedMessages.Add(reviewed);
                    }
                }
            }
            else if (options.EnableVisionOcrReview)
            {
                LogPerformance(extractionOptions, $"本轮未触发 VLM 复核，模式={options.VisionReviewMode}，待识别候选={candidates.Count}，已识别消息={recognizedMessages.Count}。");
            }
        }

        var messages = recognizedMessages
            .Select(item => item.Message)
            .ToArray();

        var source = CreateExtractionSource(messages);
        var result = ChatMessageFlowAnalyzer.CreateResult(
            messages,
            source,
            null);
        var debugStopwatch = Stopwatch.StartNew();
        var debugPath = saveDebugCapture &&
            ChatMessageDebugCapturePolicy.ShouldSave(options.EnableDebugCaptures, extractionOptions.ContinuousDebugCaptureMode, IsFlowDebugError(result))
            ? SaveDebugCapture(image, capture.Bounds, result.Messages, options, taskId)
            : null;
        if (!string.IsNullOrWhiteSpace(debugPath))
        {
            LogPerformance(extractionOptions, $"视觉消息流调试截图保存完成，耗时 {debugStopwatch.ElapsedMilliseconds} ms：{debugPath}");
            result = result with { DebugCapturePath = debugPath };
        }
        if (extractionOptions.VisualCache is not null &&
            !string.IsNullOrWhiteSpace(frameFingerprint))
        {
            extractionOptions.VisualCache.RememberFrame(
                conversationContextRegion,
                image.Width,
                image.Height,
                frameFingerprint,
                result);
        }

        LogPerformance(
            extractionOptions,
            $"视觉缓存统计：FrameSkip={runtimeStats.FrameSkipCount}，BubbleHit={runtimeStats.BubbleCacheHitCount}，BubbleMiss={runtimeStats.BubbleCacheMissCount}，OCR={runtimeStats.OcrRunCount}，VLM={runtimeStats.VisionRunCount}。");
        LogPerformance(extractionOptions, $"视觉消息流解析总耗时 {totalStopwatch.ElapsedMilliseconds} ms，{result.Summary}。");
        return result;
    }

    private async Task<List<RecognizedCandidateMessage>> RecognizeCandidatesAsync(
        Mat image,
        DrawingRectangle screenBounds,
        IReadOnlyList<MessageBubbleCandidate> candidates,
        RpaAutomationOptions options,
        Guid taskId,
        bool allowVisionReview,
        ChatMessageVisualExtractionOptions extractionOptions,
        ChatMessageVisualExtractionRuntimeStats runtimeStats,
        CancellationToken cancellationToken)
    {
        var messages = new List<RecognizedCandidateMessage>();
        for (var index = 0; index < candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var recognized = await RecognizeCandidateAsync(
                image,
                screenBounds,
                candidates[index],
                options,
                taskId,
                index,
                allowVisionReview,
                forceVisionReview: false,
                extractionOptions,
                runtimeStats,
                cancellationToken);
            if (recognized is not null)
            {
                messages.Add(recognized);
            }
        }

        return messages;
    }

    private async Task<RecognizedCandidateMessage?> RecognizeCandidateAsync(
        Mat image,
        DrawingRectangle screenBounds,
        MessageBubbleCandidate candidate,
        RpaAutomationOptions options,
        Guid taskId,
        int candidateIndex,
        bool allowVisionReview,
        bool forceVisionReview,
        ChatMessageVisualExtractionOptions extractionOptions,
        ChatMessageVisualExtractionRuntimeStats runtimeStats,
        CancellationToken cancellationToken)
    {
        var cropRect = ClampRect(candidate.Bounds, image.Width, image.Height);
        if (cropRect.Width <= 0 || cropRect.Height <= 0)
        {
            return null;
        }

        using var crop = new Mat(image, cropRect);
        var bubbleSaveStopwatch = Stopwatch.StartNew();
        var bubblePath = TrySaveMessageBubbleCrop(crop, screenBounds, cropRect, candidate, options, taskId, candidateIndex, extractionOptions, isError: false);
        if (!string.IsNullOrWhiteSpace(bubblePath))
        {
            LogPerformance(extractionOptions, $"气泡截图保存完成 #{candidateIndex + 1}，耗时 {bubbleSaveStopwatch.ElapsedMilliseconds} ms。");
        }

        if (!Cv2.ImEncode(".png", crop, out var pngBytes))
        {
            return null;
        }

        var bubbleCacheKey = ChatMessageVisualCacheKey.CreateBubbleKey(
            pngBytes,
            candidate.SenderType,
            cropRect.Width,
            cropRect.Height);
        if (TryUseBubbleCache(
            bubbleCacheKey,
            candidate,
            cropRect,
            screenBounds,
            allowVisionReview,
            forceVisionReview,
            options,
            extractionOptions,
            runtimeStats,
            out var cachedMessage))
        {
            return cachedMessage with
            {
                CandidateIndex = candidateIndex,
                Candidate = candidate,
                ScreenBounds = screenBounds,
                CropRect = cropRect,
                PngBytes = pngBytes,
                BubbleCacheKey = bubbleCacheKey
            };
        }

        if (extractionOptions.VisualCache is not null)
        {
            runtimeStats.BubbleCacheMissCount++;
        }

        extractionOptions.StageStatus?.Invoke($"正在 OCR 气泡 {candidateIndex + 1}");
        var ocrStopwatch = Stopwatch.StartNew();
        var ocr = candidate.SenderType == ChatMessageSenderType.Customer
            ? await ocrEngine.RecognizeAccurateAsync(pngBytes, cancellationToken)
            : await ocrEngine.RecognizeAsync(pngBytes, cancellationToken);
        runtimeStats.OcrRunCount++;
        LogPerformance(
            extractionOptions,
            $"气泡 OCR 完成 #{candidateIndex + 1}，发送方候选={candidate.SenderType}，来源={ocr.Source}，置信度={ocr.Confidence:0.0000}，耗时 {ocrStopwatch.ElapsedMilliseconds} ms。");

        var shouldReview = forceVisionReview ||
            allowVisionReview && VisionOcrReviewPolicy.ShouldReview(ocr, candidate.SenderType, options);
        var mergeResult = shouldReview
            ? await ReviewAndMergeAsync(
                pngBytes,
                crop,
                screenBounds,
                cropRect,
                candidate,
                options,
                taskId,
                candidateIndex,
                ocr,
                extractionOptions,
                runtimeStats,
                cancellationToken)
            : VisionOcrMergePolicy.Merge(
                ocr,
                candidate.SenderType,
                null,
                options.VisionOcrMinConfidence,
                skipWhenVisionFails: false);

        if (!mergeResult.IsUsable)
        {
            TrySaveMessageBubbleCrop(crop, screenBounds, cropRect, candidate, options, taskId, candidateIndex, extractionOptions, isError: true);
            return null;
        }

        var text = mergeResult.OcrResult.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var sender = LooksLikeSystemText(text)
            ? ChatMessageSenderType.System
            : mergeResult.SenderType;
        var message = CreateRecognizedCandidateMessage(
            candidateIndex,
            candidate,
            screenBounds,
            cropRect,
            pngBytes,
            bubbleCacheKey,
            ocr,
            mergeResult,
            sender,
            text);
        RememberBubbleCache(bubbleCacheKey, ocr, mergeResult, options, extractionOptions);
        return message;
    }

    private async Task<RecognizedCandidateMessage?> ReviewRecognizedCandidateAsync(
        RecognizedCandidateMessage recognized,
        RpaAutomationOptions options,
        Guid taskId,
        ChatMessageVisualExtractionOptions extractionOptions,
        ChatMessageVisualExtractionRuntimeStats runtimeStats,
        bool forceVisionReview,
        CancellationToken cancellationToken)
    {
        if (!forceVisionReview &&
            !VisionOcrReviewPolicy.ShouldReview(recognized.OcrResult, recognized.Candidate.SenderType, options))
        {
            return recognized;
        }

        if (TryUseBubbleCache(
            recognized.BubbleCacheKey,
            recognized.Candidate,
            recognized.CropRect,
            recognized.ScreenBounds,
            allowVisionReview: true,
            forceVisionReview,
            options,
            extractionOptions,
            runtimeStats,
            out var cachedMessage))
        {
            return cachedMessage with
            {
                CandidateIndex = recognized.CandidateIndex,
                Candidate = recognized.Candidate,
                ScreenBounds = recognized.ScreenBounds,
                CropRect = recognized.CropRect,
                PngBytes = recognized.PngBytes,
                BubbleCacheKey = recognized.BubbleCacheKey
            };
        }

        using var crop = Cv2.ImDecode(recognized.PngBytes, ImreadModes.Color);
        if (crop.Empty())
        {
            return null;
        }

        var mergeResult = await ReviewAndMergeAsync(
            recognized.PngBytes,
            crop,
            recognized.ScreenBounds,
            recognized.CropRect,
            recognized.Candidate,
            options,
            taskId,
            recognized.CandidateIndex,
            recognized.OcrResult,
            extractionOptions,
            runtimeStats,
            cancellationToken);
        if (!mergeResult.IsUsable)
        {
            return null;
        }

        var text = mergeResult.OcrResult.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var sender = LooksLikeSystemText(text)
            ? ChatMessageSenderType.System
            : mergeResult.SenderType;
        var message = CreateRecognizedCandidateMessage(
            recognized.CandidateIndex,
            recognized.Candidate,
            recognized.ScreenBounds,
            recognized.CropRect,
            recognized.PngBytes,
            recognized.BubbleCacheKey,
            recognized.OcrResult,
            mergeResult,
            sender,
            text);
        RememberBubbleCache(recognized.BubbleCacheKey, recognized.OcrResult, mergeResult, options, extractionOptions);
        return message;
    }

    private static bool TryUseBubbleCache(
        string bubbleCacheKey,
        MessageBubbleCandidate candidate,
        CvRect cropRect,
        DrawingRectangle screenBounds,
        bool allowVisionReview,
        bool forceVisionReview,
        RpaAutomationOptions options,
        ChatMessageVisualExtractionOptions extractionOptions,
        ChatMessageVisualExtractionRuntimeStats runtimeStats,
        out RecognizedCandidateMessage message)
    {
        message = null!;
        if (extractionOptions.VisualCache is null ||
            !extractionOptions.VisualCache.TryGetBubble(bubbleCacheKey, out var cacheEntry))
        {
            return false;
        }

        var requiresVisionReview = forceVisionReview ||
            allowVisionReview &&
            VisionOcrReviewPolicy.ShouldReview(cacheEntry.BaseOcrResult, candidate.SenderType, options);
        if (requiresVisionReview && !cacheEntry.MergeResult.UsedVisionReview)
        {
            return false;
        }

        if (!cacheEntry.MergeResult.IsUsable)
        {
            return false;
        }

        var text = cacheEntry.MergeResult.OcrResult.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var sender = LooksLikeSystemText(text)
            ? ChatMessageSenderType.System
            : cacheEntry.MergeResult.SenderType;
        message = CreateRecognizedCandidateMessage(
            0,
            candidate,
            screenBounds,
            cropRect,
            [],
            bubbleCacheKey,
            cacheEntry.BaseOcrResult,
            cacheEntry.MergeResult,
            sender,
            text);
        runtimeStats.BubbleCacheHitCount++;
        extractionOptions.StageStatus?.Invoke("气泡缓存命中");
        LogPerformance(extractionOptions, $"气泡缓存命中，发送方候选={candidate.SenderType}，区域={cropRect}。");
        return true;
    }

    private static RecognizedCandidateMessage CreateRecognizedCandidateMessage(
        int candidateIndex,
        MessageBubbleCandidate candidate,
        DrawingRectangle screenBounds,
        CvRect cropRect,
        byte[] pngBytes,
        string bubbleCacheKey,
        OcrResult baseOcrResult,
        VisionOcrMergeResult mergeResult,
        ChatMessageSenderType sender,
        string text)
    {
        var screenRect = ToScreenRectangle(screenBounds, cropRect);
        return new RecognizedCandidateMessage(
            candidateIndex,
            new ChatMessageItem(
                sender,
                text,
                screenRect,
                mergeResult.OcrResult.Confidence,
                0,
                mergeResult.OcrResult.Source),
            candidate,
            screenBounds,
            cropRect,
            pngBytes,
            bubbleCacheKey,
            baseOcrResult,
            mergeResult);
    }

    private static void RememberBubbleCache(
        string bubbleCacheKey,
        OcrResult baseOcrResult,
        VisionOcrMergeResult mergeResult,
        RpaAutomationOptions options,
        ChatMessageVisualExtractionOptions extractionOptions)
    {
        extractionOptions.VisualCache?.RememberBubble(
            bubbleCacheKey,
            new ChatMessageVisualBubbleCacheEntry(baseOcrResult, mergeResult),
            Math.Max(0, options.ContinuousVisualCacheMaxEntries));
    }

    private async Task<VisionOcrMergeResult> ReviewAndMergeAsync(
        byte[] pngBytes,
        Mat crop,
        DrawingRectangle screenBounds,
        CvRect cropRect,
        MessageBubbleCandidate candidate,
        RpaAutomationOptions options,
        Guid taskId,
        int candidateIndex,
        OcrResult ocr,
        ChatMessageVisualExtractionOptions extractionOptions,
        ChatMessageVisualExtractionRuntimeStats runtimeStats,
        CancellationToken cancellationToken)
    {
        var debugStopwatch = Stopwatch.StartNew();
        var debugPath = TrySaveVisionOcrDebugCapture(crop, screenBounds, cropRect, candidate, options, taskId, candidateIndex, extractionOptions, isError: false);
        if (!string.IsNullOrWhiteSpace(debugPath))
        {
            LogPerformance(extractionOptions, $"VLM 复核截图保存完成 #{candidateIndex + 1}，耗时 {debugStopwatch.ElapsedMilliseconds} ms。");
        }

        extractionOptions.StageStatus?.Invoke($"正在 VLM 复核气泡 {candidateIndex + 1}");
        var visionStopwatch = Stopwatch.StartNew();
        var vision = await visionOcrReviewer.ReviewAsync(
            pngBytes,
            ocr,
            candidate.SenderType,
            options,
            cancellationToken);
        runtimeStats.VisionRunCount++;
        LogPerformance(
            extractionOptions,
            $"VLM 复核完成 #{candidateIndex + 1}，成功={vision.Succeeded}，发送方={vision.SenderType}，置信度={vision.Confidence:0.0000}，耗时 {visionStopwatch.ElapsedMilliseconds} ms。");

        var mergeResult = VisionOcrMergePolicy.Merge(
            ocr,
            candidate.SenderType,
            vision,
            options.VisionOcrMinConfidence,
            VisionOcrReviewPolicy.ShouldSkipWhenVisionFails(options));
        if (!mergeResult.IsUsable && string.IsNullOrWhiteSpace(debugPath))
        {
            TrySaveVisionOcrDebugCapture(crop, screenBounds, cropRect, candidate, options, taskId, candidateIndex, extractionOptions, isError: true);
        }

        return mergeResult;
    }

    private static string? TrySaveMessageBubbleCrop(
        Mat crop,
        DrawingRectangle screenBounds,
        CvRect cropRect,
        MessageBubbleCandidate candidate,
        RpaAutomationOptions options,
        Guid taskId,
        int candidateIndex,
        ChatMessageVisualExtractionOptions extractionOptions,
        bool isError)
    {
        if (!ChatMessageDebugCapturePolicy.ShouldSave(options.EnableDebugCaptures, extractionOptions.ContinuousDebugCaptureMode, isError))
        {
            return null;
        }

        try
        {
            var directory = string.IsNullOrWhiteSpace(options.DebugCaptureDirectory)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AIChat",
                    "RpaClient",
                    "debug-captures")
                : Environment.ExpandEnvironmentVariables(options.DebugCaptureDirectory);

            Directory.CreateDirectory(directory);
            var fileName = $"{DateTime.Now:yyyyMMdd-HHmmssfff}-{taskId:N}-MessageBubble-{candidateIndex + 1:00}-{candidate.SenderType}-{candidate.Source}-{screenBounds.Left + cropRect.X}-{screenBounds.Top + cropRect.Y}-{cropRect.Width}x{cropRect.Height}.png";
            var path = Path.Combine(directory, fileName);
            Cv2.ImWrite(path, crop);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static string? TrySaveVisionOcrDebugCapture(
        Mat crop,
        DrawingRectangle screenBounds,
        CvRect cropRect,
        MessageBubbleCandidate candidate,
        RpaAutomationOptions options,
        Guid taskId,
        int candidateIndex,
        ChatMessageVisualExtractionOptions extractionOptions,
        bool isError)
    {
        if (!ChatMessageDebugCapturePolicy.ShouldSave(options.EnableVisionOcrDebugCaptures, extractionOptions.ContinuousDebugCaptureMode, isError))
        {
            return null;
        }

        try
        {
            var directory = string.IsNullOrWhiteSpace(options.VisionOcrDebugCaptureDirectory)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AIChat",
                    "RpaClient",
                    "vision-ocr-captures")
                : Environment.ExpandEnvironmentVariables(options.VisionOcrDebugCaptureDirectory);

            Directory.CreateDirectory(directory);
            var fileName = $"{DateTime.Now:yyyyMMdd-HHmmssfff}-{taskId:N}-VisionOcrBubble-{candidateIndex + 1:00}-{candidate.SenderType}-{candidate.Source}-{screenBounds.Left + cropRect.X}-{screenBounds.Top + cropRect.Y}-{cropRect.Width}x{cropRect.Height}.png";
            var path = Path.Combine(directory, fileName);
            Cv2.ImWrite(path, crop);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static string CreateExtractionSource(IReadOnlyList<ChatMessageItem> messages)
    {
        var reviewedCount = messages.Count(message =>
            message.OcrSource.Contains("VisionOcr:", StringComparison.OrdinalIgnoreCase));
        return reviewedCount == 0
            ? "VisualMessageFlow"
            : $"VisualMessageFlow+VisionOcr({reviewedCount})";
    }

    private static IReadOnlyList<MessageBubbleCandidate> SelectCandidatesForOcr(
        IReadOnlyList<MessageBubbleCandidate> candidates,
        Mat image,
        ChatMessageVisualExtractionOptions extractionOptions)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var bottomRatio = Math.Clamp((double)extractionOptions.BottomRegionRatio, 0.10d, 1.00d);
        var bottomStartY = (int)Math.Round(image.Height * (1d - bottomRatio));
        var scopedCandidates = candidates
            .Where(candidate => candidate.Bounds.Bottom >= bottomStartY)
            .Where(candidate => candidate.SenderType != ChatMessageSenderType.System)
            .ToArray();

        if (scopedCandidates.Length == 0)
        {
            scopedCandidates = candidates
                .Where(candidate => candidate.SenderType != ChatMessageSenderType.System)
                .ToArray();
        }

        if (extractionOptions.MaxCandidatesToRecognize <= 0 ||
            scopedCandidates.Length <= extractionOptions.MaxCandidatesToRecognize)
        {
            return scopedCandidates
                .OrderBy(candidate => candidate.Bounds.Top)
                .ThenBy(candidate => candidate.Bounds.Left)
                .ToArray();
        }

        return scopedCandidates
            .OrderByDescending(candidate => candidate.Bounds.Bottom)
            .ThenByDescending(candidate => candidate.Bounds.Right)
            .Take(extractionOptions.MaxCandidatesToRecognize)
            .OrderBy(candidate => candidate.Bounds.Top)
            .ThenBy(candidate => candidate.Bounds.Left)
            .ToArray();
    }

    private static IReadOnlyList<int> FindContinuousVisionReviewCandidateIndexes(
        IReadOnlyList<MessageBubbleCandidate> candidates,
        IReadOnlyList<RecognizedCandidateMessage> recognizedMessages,
        RpaAutomationOptions options)
    {
        if (!options.EnableVisionOcrReview)
        {
            return [];
        }

        if (IsAllRecognizedMessagesVisionReviewScope(options.ContinuousVisionReviewScope))
        {
            return Enumerable.Range(0, candidates.Count)
                .Where(index => candidates[index].SenderType != ChatMessageSenderType.System)
                .ToArray();
        }

        var indexes = FindPendingCustomerCandidateIndexes(candidates, recognizedMessages, options)
            .ToList();
        var latestEffectiveIndex = FindLatestEffectiveCandidateIndex(candidates);
        if (options.ContinuousReviewLatestSelfMessage &&
            latestEffectiveIndex >= 0 &&
            candidates[latestEffectiveIndex].SenderType == ChatMessageSenderType.Self)
        {
            indexes.Add(latestEffectiveIndex);
        }

        return indexes
            .Distinct()
            .Order()
            .ToArray();
    }

    private static bool IsAllRecognizedMessagesVisionReviewScope(string? value)
    {
        return string.Equals(value, "AllRecognizedMessages", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "AllVisibleMessages", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<int> FindPendingCustomerCandidateIndexes(
        IReadOnlyList<MessageBubbleCandidate> candidates,
        IReadOnlyList<RecognizedCandidateMessage> recognizedMessages,
        RpaAutomationOptions options)
    {
        if (!options.EnableVisionOcrReview)
        {
            return [];
        }

        var visualPendingIndexes = FindVisualPendingCustomerCandidateIndexes(candidates);
        if (visualPendingIndexes.Count == 0)
        {
            return [];
        }

        var preliminaryResult = ChatMessageFlowAnalyzer.CreateResult(
            recognizedMessages.Select(item => item.Message).ToArray(),
            "VisualMessageFlowPreliminary");
        if (preliminaryResult.PendingCustomerMessageGroup is not null)
        {
            var groupBounds = preliminaryResult.PendingCustomerMessageGroup.Messages
                .Select(message => message.Bounds)
                .ToArray();
            var indexes = recognizedMessages
                .Where(item => groupBounds.Any(bounds => bounds.Equals(item.Message.Bounds)))
                .Select(item => item.CandidateIndex)
                .Distinct()
                .Order()
                .ToArray();

            if (indexes.Length > 0)
            {
                visualPendingIndexes = visualPendingIndexes
                    .Union(indexes)
                    .Distinct()
                    .Order()
                    .ToArray();
            }
        }

        return visualPendingIndexes
            .Where(index => NeedsVisionReviewSecondPass(index, candidates, recognizedMessages, options))
            .ToArray();
    }

    private static bool IsLatestSelfCandidate(
        int candidateIndex,
        IReadOnlyList<MessageBubbleCandidate> candidates)
    {
        return candidateIndex >= 0 &&
            candidateIndex == FindLatestEffectiveCandidateIndex(candidates) &&
            candidateIndex < candidates.Count &&
            candidates[candidateIndex].SenderType == ChatMessageSenderType.Self;
    }

    private static int FindLatestEffectiveCandidateIndex(IReadOnlyList<MessageBubbleCandidate> candidates)
    {
        for (var index = candidates.Count - 1; index >= 0; index--)
        {
            if (candidates[index].SenderType == ChatMessageSenderType.System)
            {
                continue;
            }

            return index;
        }

        return -1;
    }

    private static IReadOnlyList<int> FindVisualPendingCustomerCandidateIndexes(
        IReadOnlyList<MessageBubbleCandidate> candidates)
    {
        var latestEffectiveCandidateIndex = FindLatestEffectiveCandidateIndex(candidates);

        if (latestEffectiveCandidateIndex < 0 ||
            candidates[latestEffectiveCandidateIndex].SenderType == ChatMessageSenderType.Self)
        {
            return [];
        }

        var pendingIndexes = new List<int>();
        for (var index = latestEffectiveCandidateIndex; index >= 0; index--)
        {
            var sender = candidates[index].SenderType;
            if (sender == ChatMessageSenderType.System)
            {
                continue;
            }

            if (sender is ChatMessageSenderType.Customer or ChatMessageSenderType.Unknown)
            {
                pendingIndexes.Add(index);
                continue;
            }

            break;
        }

        pendingIndexes.Reverse();
        return pendingIndexes;
    }

    private static bool NeedsVisionReviewSecondPass(
        int candidateIndex,
        IReadOnlyList<MessageBubbleCandidate> candidates,
        IReadOnlyList<RecognizedCandidateMessage> recognizedMessages,
        RpaAutomationOptions options)
    {
        var candidate = candidates[candidateIndex];
        var recognized = recognizedMessages.FirstOrDefault(item => item.CandidateIndex == candidateIndex);
        if (recognized is null)
        {
            return candidate.SenderType is ChatMessageSenderType.Customer or ChatMessageSenderType.Unknown;
        }

        return VisionOcrReviewPolicy.ShouldReview(
            new OcrResult(
                recognized.Message.Text,
                recognized.Message.OcrConfidence,
                recognized.Message.OcrSource),
            recognized.Message.SenderType,
            options);
    }

    private static void LogPerformance(ChatMessageVisualExtractionOptions options, string message)
    {
        options.DiagnosticsLog?.Invoke($"[性能] {message}");
    }

    private static string AppendSource(string source, string suffix)
    {
        return source.Contains(suffix, StringComparison.OrdinalIgnoreCase)
            ? source
            : $"{source}+{suffix}";
    }

    private static string? TryCreateFrameFingerprint(Mat image, decimal bottomRatio)
    {
        var ratio = Math.Clamp((double)bottomRatio, 0.10d, 1.00d);
        var top = (int)Math.Round(image.Height * (1d - ratio));
        var cropRect = CvRect.FromLTRB(0, Math.Clamp(top, 0, Math.Max(0, image.Height - 1)), image.Width, image.Height);
        if (cropRect.Width <= 0 || cropRect.Height <= 0)
        {
            return null;
        }

        using var crop = new Mat(image, cropRect);
        return Cv2.ImEncode(".png", crop, out var pngBytes)
            ? ChatMessageVisualCacheKey.CreateFrameKey(pngBytes)
            : null;
    }

    private static bool IsFlowDebugError(ChatMessageVisualExtractionResult result)
    {
        return result.Messages.Count == 0 ||
            result.LatestEffectiveMessage?.SenderType == ChatMessageSenderType.Unknown ||
            result.OcrConfidence <= 0;
    }

    private static IReadOnlyList<MessageBubbleCandidate> DetectCandidates(Mat image)
    {
        var selfCandidates = DetectSelfBubbleCandidates(image);
        var candidates = new List<MessageBubbleCandidate>(selfCandidates);
        candidates.AddRange(DetectTextDrivenCandidates(image, selfCandidates));

        var expandedCandidates = MergeCandidates(candidates)
            .Select(candidate => ExpandCustomerTextCandidateForVision(candidate, image.Width, image.Height))
            .ToArray();

        return MergeCandidates(expandedCandidates)
            .OrderBy(candidate => candidate.Bounds.Top)
            .ThenBy(candidate => candidate.Bounds.Left)
            .ToArray();
    }

    private static IReadOnlyList<MessageBubbleCandidate> DetectSelfBubbleCandidates(Mat image)
    {
        using var hsv = new Mat();
        using var mask = new Mat();
        Cv2.CvtColor(image, hsv, ColorConversionCodes.BGR2HSV);
        Cv2.InRange(hsv, new Scalar(35, 35, 100), new Scalar(90, 255, 255), mask);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new CvSize(15, 9));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel, iterations: 2);

        var candidates = new List<MessageBubbleCandidate>();
        var contours = Cv2.FindContoursAsArray(mask, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        foreach (var contour in contours)
        {
            var rect = Cv2.BoundingRect(contour);
            if (rect.Width < 32 ||
                rect.Height < 20 ||
                rect.Width * rect.Height < 500 ||
                CenterX(rect) < image.Width * 0.45)
            {
                continue;
            }

            candidates.Add(new MessageBubbleCandidate(
                ExpandRect(rect, 8, 8, image.Width, image.Height),
                ChatMessageSenderType.Self,
                "GreenBubble"));
        }

        return candidates;
    }

    private static IReadOnlyList<MessageBubbleCandidate> DetectTextDrivenCandidates(
        Mat image,
        IReadOnlyList<MessageBubbleCandidate> selfCandidates)
    {
        using var gray = new Mat();
        using var mask = new Mat();
        Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.InRange(gray, new Scalar(0), new Scalar(170), mask);
        using var closeKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new CvSize(10, 4));
        using var dilateKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new CvSize(8, 4));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, closeKernel, iterations: 1);
        Cv2.Dilate(mask, mask, dilateKernel, iterations: 1);

        var candidates = new List<MessageBubbleCandidate>();
        var contours = Cv2.FindContoursAsArray(mask, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        foreach (var contour in contours)
        {
            var rect = Cv2.BoundingRect(contour);
            if (!LooksLikeTextRect(rect, image.Width))
            {
                continue;
            }

            if (selfCandidates.Any(candidate => Intersects(candidate.Bounds, rect)))
            {
                continue;
            }

            var sender = ClassifyTextRect(rect, image.Width);
            if (LooksLikeAvatarOrIconRect(rect, image.Width))
            {
                continue;
            }

            if (sender == ChatMessageSenderType.Unknown && rect.Height < 12)
            {
                continue;
            }

            candidates.Add(new MessageBubbleCandidate(
                ExpandRect(rect, 34, 18, image.Width, image.Height),
                sender,
                "TextRegion"));
        }

        return candidates;
    }

    private static IReadOnlyList<MessageBubbleCandidate> MergeCandidates(IReadOnlyList<MessageBubbleCandidate> candidates)
    {
        var ordered = candidates
            .OrderBy(candidate => candidate.Bounds.Top)
            .ThenBy(candidate => candidate.Bounds.Left)
            .ToList();
        var merged = new List<MessageBubbleCandidate>();

        foreach (var candidate in ordered)
        {
            var lastIndex = merged.FindLastIndex(existing => ShouldMerge(existing, candidate));
            if (lastIndex < 0)
            {
                merged.Add(candidate);
                continue;
            }

            var existing = merged[lastIndex];
            merged[lastIndex] = existing with
            {
                Bounds = Union(existing.Bounds, candidate.Bounds),
                Source = $"{existing.Source}+{candidate.Source}"
            };
        }

        return merged;
    }

    private static MessageBubbleCandidate ExpandCustomerTextCandidateForVision(
        MessageBubbleCandidate candidate,
        int imageWidth,
        int imageHeight)
    {
        if (candidate.SenderType is not (ChatMessageSenderType.Customer or ChatMessageSenderType.Unknown) ||
            !candidate.Source.Contains("TextRegion", StringComparison.OrdinalIgnoreCase) ||
            candidate.Bounds.Left > imageWidth * 0.45)
        {
            return candidate;
        }

        var maxCustomerMessageRight = (int)Math.Round(imageWidth * 0.72);
        var targetWidth = Math.Max(candidate.Bounds.Width, (int)Math.Round(imageWidth * 0.62));
        var right = Math.Min(imageWidth, Math.Max(candidate.Bounds.Right, Math.Min(maxCustomerMessageRight, candidate.Bounds.Left + targetWidth)));
        var expanded = CvRect.FromLTRB(
            Math.Max(0, candidate.Bounds.Left - 12),
            Math.Max(0, candidate.Bounds.Top - 4),
            right,
            Math.Min(imageHeight, candidate.Bounds.Bottom + 4));

        return candidate with
        {
            Bounds = expanded,
            Source = candidate.Source.Contains("CustomerLane", StringComparison.OrdinalIgnoreCase)
                ? candidate.Source
                : $"{candidate.Source}+CustomerLane"
        };
    }

    private static bool ShouldMerge(MessageBubbleCandidate first, MessageBubbleCandidate second)
    {
        if (first.SenderType != second.SenderType)
        {
            return false;
        }

        if (first.SenderType is ChatMessageSenderType.Customer or ChatMessageSenderType.Self &&
            first.Source.Contains("TextRegion", StringComparison.OrdinalIgnoreCase) &&
            second.Source.Contains("TextRegion", StringComparison.OrdinalIgnoreCase) &&
            LooksLikeStackedSeparateMessages(first.Bounds, second.Bounds))
        {
            return false;
        }

        var verticalGap = Math.Max(0, Math.Max(first.Bounds.Top, second.Bounds.Top) - Math.Min(first.Bounds.Bottom, second.Bounds.Bottom));
        if (verticalGap > 18)
        {
            return false;
        }

        if (first.SenderType == ChatMessageSenderType.System)
        {
            return true;
        }

        var horizontalOverlap = Math.Min(first.Bounds.Right, second.Bounds.Right) - Math.Max(first.Bounds.Left, second.Bounds.Left);
        return horizontalOverlap > -24;
    }

    private static bool LooksLikeStackedSeparateMessages(CvRect first, CvRect second)
    {
        var verticalOverlap = Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top);
        if (verticalOverlap > Math.Min(first.Height, second.Height) * 0.45)
        {
            return false;
        }

        var centerGap = Math.Abs(CenterY(first) - CenterY(second));
        if (centerGap < Math.Min(first.Height, second.Height) * 0.55)
        {
            return false;
        }

        var horizontalOverlap = Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left);
        var minWidth = Math.Min(first.Width, second.Width);
        return horizontalOverlap > minWidth * 0.35;
    }

    private static bool LooksLikeTextRect(CvRect rect, int imageWidth)
    {
        if (rect.Width < 4 ||
            rect.Height < 5 ||
            rect.Width * rect.Height < 40 ||
            rect.Height > 90 ||
            rect.Width > imageWidth * 0.60)
        {
            return false;
        }

        return true;
    }

    private static ChatMessageSenderType ClassifyTextRect(CvRect rect, int imageWidth)
    {
        var centerX = CenterX(rect);
        if (centerX < imageWidth * 0.45 && rect.Right > Math.Max(56, imageWidth * 0.015))
        {
            return ChatMessageSenderType.Customer;
        }

        if (centerX > imageWidth * 0.36 &&
            centerX < imageWidth * 0.64 &&
            rect.Height < 48 &&
            rect.Width < imageWidth * 0.38)
        {
            return ChatMessageSenderType.System;
        }

        return ChatMessageSenderType.Unknown;
    }

    private static bool LooksLikeAvatarOrIconRect(CvRect rect, int imageWidth)
    {
        if (rect.Height < 24 ||
            rect.Height > 88 ||
            rect.Width >= 76)
        {
            return false;
        }

        var nearLeftAvatarColumn = rect.X < 64 &&
            rect.Width < 72 &&
            rect.Height > 24;
        var nearRightAvatarColumn = rect.Right > imageWidth - 72 &&
            rect.Width < 72 &&
            rect.Height > 24;

        return nearLeftAvatarColumn || nearRightAvatarColumn;
    }

    private static bool LooksLikeSystemText(string text)
    {
        if (CustomerMessageExtractor.IsWeChatSystemNotice(text))
        {
            return true;
        }

        var compact = new string(text.Where(value => !char.IsWhiteSpace(value)).ToArray());
        var normalized = compact.Replace('：', ':');
        var parts = normalized.Split(':');
        if (parts.Length == 2 &&
            parts[0].Length is >= 1 and <= 2 &&
            parts[1].Length == 2 &&
            parts[0].All(char.IsDigit) &&
            parts[1].All(char.IsDigit))
        {
            return true;
        }

        return normalized.Contains("你已添加", StringComparison.Ordinal) ||
            normalized.Contains("现在可以开始聊天", StringComparison.Ordinal);
    }

    private static string? SaveDebugCapture(
        Mat image,
        DrawingRectangle screenBounds,
        IReadOnlyList<ChatMessageItem> messages,
        RpaAutomationOptions options,
        Guid taskId)
    {
        if (!options.EnableDebugCaptures)
        {
            return null;
        }

        try
        {
            var directory = string.IsNullOrWhiteSpace(options.DebugCaptureDirectory)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AIChat",
                    "RpaClient",
                    "debug-captures")
                : Environment.ExpandEnvironmentVariables(options.DebugCaptureDirectory);

            Directory.CreateDirectory(directory);
            using var annotated = image.Clone();
            foreach (var message in messages)
            {
                var color = message.SenderType switch
                {
                    ChatMessageSenderType.Customer => new Scalar(255, 80, 80),
                    ChatMessageSenderType.Self => new Scalar(0, 210, 0),
                    ChatMessageSenderType.System => new Scalar(160, 160, 160),
                    _ => new Scalar(0, 140, 255)
                };
                var rect = new CvRect(
                    message.Bounds.X - screenBounds.Left,
                    message.Bounds.Y - screenBounds.Top,
                    message.Bounds.Width,
                    message.Bounds.Height);
                Cv2.Rectangle(annotated, rect, color, 2);
                Cv2.PutText(
                    annotated,
                    $"{message.VisualOrder}:{message.SenderDisplayName}",
                    new CvPoint(rect.X, Math.Max(18, rect.Y - 6)),
                    HersheyFonts.HersheySimplex,
                    0.55,
                    color,
                    1);
            }

            var fileName = $"{DateTime.Now:yyyyMMdd-HHmmssfff}-{taskId:N}-ChatMessageFlow.png";
            var path = Path.Combine(directory, fileName);
            Cv2.ImWrite(path, annotated);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static CvRect ClampRect(CvRect rect, int imageWidth, int imageHeight)
    {
        var left = Math.Clamp(rect.Left, 0, Math.Max(0, imageWidth - 1));
        var top = Math.Clamp(rect.Top, 0, Math.Max(0, imageHeight - 1));
        var right = Math.Clamp(rect.Right, left + 1, imageWidth);
        var bottom = Math.Clamp(rect.Bottom, top + 1, imageHeight);
        return CvRect.FromLTRB(left, top, right, bottom);
    }

    private static CvRect ExpandRect(CvRect rect, int paddingX, int paddingY, int imageWidth, int imageHeight)
    {
        return CvRect.FromLTRB(
            Math.Max(0, rect.Left - paddingX),
            Math.Max(0, rect.Top - paddingY),
            Math.Min(imageWidth, rect.Right + paddingX),
            Math.Min(imageHeight, rect.Bottom + paddingY));
    }

    private static CvRect Union(CvRect first, CvRect second)
    {
        return CvRect.FromLTRB(
            Math.Min(first.Left, second.Left),
            Math.Min(first.Top, second.Top),
            Math.Max(first.Right, second.Right),
            Math.Max(first.Bottom, second.Bottom));
    }

    private static DrawingRectangle ToScreenRectangle(DrawingRectangle screenBounds, CvRect localRect)
    {
        return new DrawingRectangle(
            screenBounds.Left + localRect.X,
            screenBounds.Top + localRect.Y,
            localRect.Width,
            localRect.Height);
    }

    private static bool Intersects(CvRect first, CvRect second)
    {
        return first.Left < second.Right &&
            first.Right > second.Left &&
            first.Top < second.Bottom &&
            first.Bottom > second.Top;
    }

    private static double CenterX(CvRect rect)
    {
        return rect.X + rect.Width / 2d;
    }

    private static double CenterY(CvRect rect)
    {
        return rect.Y + rect.Height / 2d;
    }

    private sealed record MessageBubbleCandidate(
        CvRect Bounds,
        ChatMessageSenderType SenderType,
        string Source);

    private sealed record RecognizedCandidateMessage(
        int CandidateIndex,
        ChatMessageItem Message,
        MessageBubbleCandidate Candidate,
        DrawingRectangle ScreenBounds,
        CvRect CropRect,
        byte[] PngBytes,
        string BubbleCacheKey,
        OcrResult OcrResult,
        VisionOcrMergeResult MergeResult);
}

public sealed record ChatMessageVisualExtractionOptions(
    int MaxCandidatesToRecognize,
    decimal BottomRegionRatio,
    bool ReviewOnlyPendingCustomerGroup,
    Action<string>? DiagnosticsLog,
    Action<string>? StageStatus,
    ChatMessageVisualCache? VisualCache = null,
    bool EnableUnchangedFrameSkip = false,
    decimal UnchangedFrameBottomRatio = 0.45m,
    string ContinuousDebugCaptureMode = "Always")
{
    public static ChatMessageVisualExtractionOptions Default { get; } = new(
        0,
        1m,
        false,
        null,
        null);
}

public sealed class ChatMessageVisualExtractionRuntimeStats
{
    public int FrameSkipCount { get; set; }
    public int BubbleCacheHitCount { get; set; }
    public int BubbleCacheMissCount { get; set; }
    public int OcrRunCount { get; set; }
    public int VisionRunCount { get; set; }
}
