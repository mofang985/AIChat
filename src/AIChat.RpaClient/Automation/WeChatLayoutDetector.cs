using System.Drawing;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using AIChat.RpaClient.Configuration;
using OpenCvSharp;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using CvPoint = OpenCvSharp.Point;
using CvRect = OpenCvSharp.Rect;

namespace AIChat.RpaClient.Automation;

public sealed class WeChatLayoutDetector(ScreenCaptureService screenCaptureService)
{
    public async Task<WeChatLayoutResult> DetectAsync(
        WeChatWindow window,
        RpaAutomationOptions options,
        Guid taskId,
        CancellationToken cancellationToken,
        bool saveDebugCapture = true)
    {
        var manualLayout = TryCreateManualLayout(window.ClientBounds, options);
        if (options.CoordinateMode.Equals("Manual", StringComparison.OrdinalIgnoreCase))
        {
            return manualLayout?.WithMode("配置坐标", "CoordinateMode=Manual")
                ?? WeChatLayoutResult.Failed("配置坐标无效。");
        }

        CapturedImage? capture = null;
        Mat? image = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            capture = screenCaptureService.Capture(window.ClientBounds);
            image = Cv2.ImDecode(capture.PngBytes, ImreadModes.Color);
            if (image.Empty())
            {
                return FallbackOrFailed(manualLayout, "微信客户区截图为空。");
            }

            var detectedLayout = await TryDetectLayoutAsync(
                window.ClientBounds,
                image,
                capture.PngBytes,
                options,
                cancellationToken);
            if (detectedLayout is not null &&
                detectedLayout.Confidence >= options.LayoutDetectionMinConfidence &&
                detectedLayout.IsUsable)
            {
                var debugPath = saveDebugCapture
                    ? SaveLayoutDebugCapture(image, detectedLayout, window.ClientBounds, options, taskId)
                    : null;
                return detectedLayout with { DebugCapturePath = debugPath };
            }

            var reason = detectedLayout is null
                ? "未识别到稳定的输入区和发送按钮。"
                : !detectedLayout.IsUsable
                    ? detectedLayout.Reason
                    : $"自动定位置信度不足：{detectedLayout.Confidence:0.00}。{detectedLayout.Reason}";

            if (options.CoordinateMode.Equals("AutoOnly", StringComparison.OrdinalIgnoreCase))
            {
                if (detectedLayout is not null && detectedLayout.HasValidGeometry(window.ClientBounds))
                {
                    var failedDebugPath = saveDebugCapture
                        ? SaveLayoutDebugCapture(image, detectedLayout, window.ClientBounds, options, taskId)
                        : null;
                    return detectedLayout with
                    {
                        IsUsable = false,
                        Mode = detectedLayout.Mode.Contains("失败", StringComparison.OrdinalIgnoreCase)
                            ? detectedLayout.Mode
                            : "定位失败",
                        DebugCapturePath = failedDebugPath,
                        Reason = reason
                    };
                }

                return WeChatLayoutResult.Failed(reason);
            }

            var fallbackLayout = FallbackOrFailed(manualLayout, reason);
            var fallbackDebugPath = saveDebugCapture && fallbackLayout.IsUsable
                ? SaveLayoutDebugCapture(image, fallbackLayout, window.ClientBounds, options, taskId)
                : null;

            return fallbackLayout with { DebugCapturePath = fallbackDebugPath };
        }
        catch (Exception ex)
        {
            if (options.CoordinateMode.Equals("AutoOnly", StringComparison.OrdinalIgnoreCase))
            {
                return WeChatLayoutResult.Failed($"自动定位异常：{ex.Message}");
            }

            return FallbackOrFailed(manualLayout, $"自动定位异常：{ex.Message}");
        }
        finally
        {
            image?.Dispose();
            capture?.Dispose();
        }
    }

    private static async Task<WeChatLayoutResult?> TryDetectLayoutAsync(
        DrawingRectangle clientBounds,
        Mat image,
        byte[] imageBytes,
        RpaAutomationOptions options,
        CancellationToken cancellationToken)
    {
        var width = image.Width;
        var height = image.Height;
        if (width < 800 || height < 600)
        {
            return WeChatLayoutResult.Failed($"微信客户区尺寸过小：{width}x{height}。");
        }

        var chatLeft = DetectChatPaneLeft(image, options);
        var sendButton = await DetectSendButtonAsync(image, imageBytes, chatLeft.X, cancellationToken);
        var rawCandidates = GenerateLayoutCandidates(image, chatLeft.X, sendButton);
        if (rawCandidates.Count == 0)
        {
            return WeChatLayoutResult.Failed($"布局候选不足：client={width}x{height}; chatLeft={chatLeft.X}({chatLeft.Source}); sendButton={Format(sendButton)}。");
        }

        var evaluations = new List<LayoutCandidateEvaluation>();
        foreach (var rawCandidate in rawCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var inputTop = new DetectedY(rawCandidate.InputTopY, rawCandidate.Source);
            var candidateSendButton = sendButton ?? InferSendButtonFromInputTop(image, inputTop);
            var candidate = rawCandidate with
            {
                HasSendButton = candidateSendButton is not null,
                SendButtonAlignmentScore = CalculateSendButtonAlignmentScore(rawCandidate.InputTopY, image.Height, candidateSendButton)
            };
            var panelLayout = CreatePanelLayout(image, chatLeft, inputTop);
            var regions = CreateLayoutRegions(clientBounds, image, panelLayout, candidateSendButton, options);
            var geometry = CreateCandidateGeometry(image, regions);
            var score = WeChatLayoutCandidateScorer.Score(candidate, geometry);
            var result = CreateLayoutResult(
                clientBounds,
                chatLeft,
                inputTop,
                panelLayout,
                regions,
                candidateSendButton,
                score,
                rawCandidates.Count);

            evaluations.Add(new LayoutCandidateEvaluation(
                candidate,
                score,
                panelLayout,
                regions,
                candidateSendButton,
                result));
        }

        var debugCandidates = evaluations
            .OrderByDescending(item => item.Score.Confidence)
            .Take(10)
            .Select(item => new WeChatLayoutDebugCandidate(
                item.Candidate.InputTopY,
                item.Candidate.Source,
                item.Score.Confidence,
                item.Score.IsSafe,
                item.Score.Reason))
            .ToArray();

        var bestAccepted = evaluations
            .Where(item => item.Result.HasValidGeometry(clientBounds))
            .Where(item => item.Score.IsAccepted(options.LayoutDetectionMinConfidence))
            .OrderByDescending(item => item.Score.Confidence)
            .FirstOrDefault();
        if (bestAccepted is not null)
        {
            return bestAccepted.Result with { DebugCandidates = debugCandidates };
        }

        var best = evaluations
            .Where(item => item.Result.HasValidGeometry(clientBounds))
            .OrderByDescending(item => item.Score.Confidence)
            .FirstOrDefault();
        if (best is not null)
        {
            return best.Result with
            {
                DebugCandidates = debugCandidates,
                Reason = $"候选分数不足或安全校验未通过：candidateCount={rawCandidates.Count}; best={best.Score.Reason}"
            };
        }

        var bestUnsafe = evaluations
            .OrderByDescending(item => item.Score.Confidence)
            .FirstOrDefault();
        return WeChatLayoutResult.Failed(
            bestUnsafe is null
                ? $"布局候选不足：candidateCount={rawCandidates.Count}; client={width}x{height}。"
                : $"布局候选几何无效：candidateCount={rawCandidates.Count}; best={bestUnsafe.Score.Reason}") with
        {
            DebugCandidates = debugCandidates
        };
    }

    private static IReadOnlyList<WeChatLayoutCandidate> GenerateLayoutCandidates(
        Mat image,
        int chatLeft,
        DetectedSendButton? sendButton)
    {
        using var gray = new Mat();
        Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);

        var seeds = new List<InputTopSeed>();
        seeds.AddRange(DetectHorizontalLineCandidateSeeds(gray, chatLeft));
        if (DetectWhiteAreaTransitionSeed(gray, chatLeft) is { } whiteAreaSeed)
        {
            seeds.Add(whiteAreaSeed);
        }

        seeds.AddRange(CreateSendButtonCandidateSeeds(gray, chatLeft, sendButton));
        seeds.AddRange(CreateFallbackRatioSeeds(gray, chatLeft));

        var candidates = seeds
            .Select(seed => BuildLayoutCandidate(image, gray, chatLeft, seed, sendButton))
            .Where(candidate => candidate.InputTopY > 0)
            .ToList();

        return MergeLayoutCandidates(candidates);
    }

    private static IReadOnlyList<InputTopSeed> DetectHorizontalLineCandidateSeeds(Mat gray, int chatLeft)
    {
        var minY = Math.Max(1, (int)(gray.Height * 0.55d));
        var maxY = Math.Min(gray.Height - 3, (int)(gray.Height * 0.92d));
        var startX = Math.Clamp(chatLeft + 10, 0, gray.Width - 2);
        var endX = Math.Clamp(gray.Width - 36, startX + 120, gray.Width - 1);
        var rows = new List<(int Y, HorizontalBoundaryStats Stats, double Quality)>();

        for (var y = minY; y <= maxY; y++)
        {
            var stats = CalculateHorizontalBoundaryStats(gray, y, startX, endX);
            var quality = stats.HorizontalCoverage * 14d + stats.RowContrast;
            if (stats.HorizontalCoverage >= 0.46d && stats.RowContrast >= 2.4d)
            {
                rows.Add((y, stats, quality));
            }
        }

        if (rows.Count == 0)
        {
            return [];
        }

        var grouped = new List<InputTopSeed>();
        var group = new List<(int Y, HorizontalBoundaryStats Stats, double Quality)>();
        foreach (var row in rows.OrderBy(row => row.Y))
        {
            if (group.Count == 0 || row.Y - group[^1].Y <= 8)
            {
                group.Add(row);
                continue;
            }

            grouped.Add(CreateBestHorizontalSeed(group));
            group.Clear();
            group.Add(row);
        }

        if (group.Count > 0)
        {
            grouped.Add(CreateBestHorizontalSeed(group));
        }

        return grouped
            .OrderByDescending(seed => seed.Stats.HorizontalCoverage * 12d + seed.Stats.RowContrast)
            .Take(8)
            .ToArray();
    }

    private static InputTopSeed CreateBestHorizontalSeed(
        IReadOnlyList<(int Y, HorizontalBoundaryStats Stats, double Quality)> group)
    {
        var best = group.OrderByDescending(row => row.Quality).First();
        return new InputTopSeed(best.Y, "HorizontalLine", best.Stats);
    }

    private static InputTopSeed? DetectWhiteAreaTransitionSeed(Mat gray, int chatLeft)
    {
        var minY = Math.Max(1, (int)(gray.Height * 0.55d));
        var maxY = Math.Min(gray.Height - 3, (int)(gray.Height * 0.92d));
        var startX = Math.Clamp(chatLeft + 10, 0, gray.Width - 2);
        var endX = Math.Clamp(gray.Width - 36, startX + 120, gray.Width - 1);
        var sampleHeight = Math.Clamp(gray.Height / 14, 64, 160);
        var bestY = 0;
        var bestScore = double.MinValue;

        for (var y = minY; y <= maxY; y += 4)
        {
            var aboveWhite = CalculateWhiteRatio(gray, startX, endX, y - sampleHeight, y - 12);
            var belowWhite = CalculateWhiteRatio(gray, startX, endX, y + 12, Math.Min(gray.Height - 44, y + sampleHeight));
            var inputHeightRatio = (gray.Height - y) / (double)gray.Height;
            var heightScore = 1d - Math.Abs(inputHeightRatio - 0.22d) / 0.16d;
            var score = (belowWhite - aboveWhite) * 0.70d + belowWhite * 0.35d + Math.Clamp(heightScore, 0d, 1d) * 0.15d;
            if (belowWhite >= 0.66d && score > bestScore)
            {
                bestScore = score;
                bestY = y;
            }
        }

        if (bestY <= 0 || bestScore < 0.40d)
        {
            return null;
        }

        return new InputTopSeed(
            bestY,
            "WhiteAreaTransition",
            CalculateHorizontalBoundaryStats(gray, bestY, startX, endX));
    }

    private static IReadOnlyList<InputTopSeed> CreateSendButtonCandidateSeeds(
        Mat gray,
        int chatLeft,
        DetectedSendButton? sendButton)
    {
        if (sendButton is null)
        {
            return [];
        }

        var minY = Math.Max(1, (int)(gray.Height * 0.55d));
        var maxY = Math.Min(gray.Height - 3, (int)(gray.Height * 0.92d));
        var startX = Math.Clamp(chatLeft + 10, 0, gray.Width - 2);
        var endX = Math.Clamp(gray.Width - 36, startX + 120, gray.Width - 1);
        var offsets = new[]
        {
            Math.Clamp((int)(gray.Height * 0.15d), 120, 260),
            Math.Clamp((int)(gray.Height * 0.19d), 150, 340),
            Math.Clamp((int)(gray.Height * 0.23d), 180, 430)
        };

        return offsets
            .Select(offset => Math.Clamp(sendButton.Rect.Top - offset, minY, maxY))
            .Distinct()
            .Select(y => new InputTopSeed(
                y,
                "SendButtonInference",
                CalculateHorizontalBoundaryStats(gray, y, startX, endX)))
            .ToArray();
    }

    private static IReadOnlyList<InputTopSeed> CreateFallbackRatioSeeds(Mat gray, int chatLeft)
    {
        var minY = Math.Max(1, (int)(gray.Height * 0.55d));
        var maxY = Math.Min(gray.Height - 3, (int)(gray.Height * 0.92d));
        var startX = Math.Clamp(chatLeft + 10, 0, gray.Width - 2);
        var endX = Math.Clamp(gray.Width - 36, startX + 120, gray.Width - 1);
        var ratios = new[] { 0.18d, 0.22d, 0.28d, 0.32d };

        return ratios
            .Select(ratio => Math.Clamp((int)(gray.Height * (1d - ratio)), minY, maxY))
            .Distinct()
            .Select(y => new InputTopSeed(
                y,
                "FallbackRatio",
                CalculateHorizontalBoundaryStats(gray, y, startX, endX)))
            .ToArray();
    }

    private static WeChatLayoutCandidate BuildLayoutCandidate(
        Mat image,
        Mat gray,
        int chatLeft,
        InputTopSeed seed,
        DetectedSendButton? sendButton)
    {
        var minY = Math.Max(1, (int)(image.Height * 0.55d));
        var maxY = Math.Min(image.Height - 3, (int)(image.Height * 0.92d));
        var inputTopY = Math.Clamp(seed.Y, minY, maxY);
        var startX = Math.Clamp(chatLeft + 10, 0, image.Width - 2);
        var endX = Math.Clamp(image.Width - 36, startX + 120, image.Width - 1);
        var inputWhiteRatio = CalculateWhiteRatio(gray, startX, endX, inputTopY + 8, image.Height - 48);
        var headerBottom = CalculateHeaderBottom(inputTopY);
        var inputHeightRatio = (image.Height - inputTopY) / (double)image.Height;
        var chatHeightRatio = Math.Max(0, inputTopY - headerBottom) / (double)image.Height;

        return new WeChatLayoutCandidate(
            inputTopY,
            seed.Source,
            seed.Stats.HorizontalCoverage,
            seed.Stats.RowContrast,
            inputWhiteRatio,
            inputHeightRatio,
            chatHeightRatio,
            CountMessageLikeRegions(image, chatLeft, inputTopY),
            sendButton is not null,
            CalculateSendButtonAlignmentScore(inputTopY, image.Height, sendButton));
    }

    private static IReadOnlyList<WeChatLayoutCandidate> MergeLayoutCandidates(
        IReadOnlyList<WeChatLayoutCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var merged = new List<WeChatLayoutCandidate>();
        foreach (var candidate in candidates.OrderBy(candidate => candidate.InputTopY))
        {
            var existingIndex = merged.FindIndex(existing => Math.Abs(existing.InputTopY - candidate.InputTopY) <= 10);
            if (existingIndex < 0)
            {
                merged.Add(candidate);
                continue;
            }

            if (CalculateCandidatePreScore(candidate) > CalculateCandidatePreScore(merged[existingIndex]))
            {
                merged[existingIndex] = candidate;
            }
        }

        return merged
            .OrderByDescending(CalculateCandidatePreScore)
            .Take(14)
            .ToArray();
    }

    private static double CalculateCandidatePreScore(WeChatLayoutCandidate candidate)
    {
        var heightScore = 1d - Math.Abs(candidate.InputHeightRatio - 0.22d) / 0.16d;
        return
            candidate.HorizontalCoverage * 1.8d +
            Math.Clamp(candidate.RowContrast / 10d, 0d, 1d) +
            candidate.InputWhiteRatio * 1.3d +
            Math.Clamp(heightScore, 0d, 1d) +
            candidate.SendButtonAlignmentScore * 0.8d;
    }

    private static HorizontalBoundaryStats CalculateHorizontalBoundaryStats(
        Mat gray,
        int y,
        int startX,
        int endX)
    {
        var clampedY = Math.Clamp(y, 1, gray.Height - 2);
        var clampedStartX = Math.Clamp(startX, 0, gray.Width - 2);
        var clampedEndX = Math.Clamp(endX, clampedStartX + 1, gray.Width - 1);
        var edgeSamples = 0;
        var samples = 0;
        var totalDiff = 0d;

        for (var x = clampedStartX; x < clampedEndX; x += 4)
        {
            var diffUp = Math.Abs(gray.At<byte>(clampedY, x) - gray.At<byte>(clampedY - 1, x));
            var diffDown = Math.Abs(gray.At<byte>(clampedY + 1, x) - gray.At<byte>(clampedY, x));
            var diff = Math.Max(diffUp, diffDown);
            totalDiff += diff;
            if (diff >= 3)
            {
                edgeSamples++;
            }

            samples++;
        }

        return samples == 0
            ? new HorizontalBoundaryStats(0, 0)
            : new HorizontalBoundaryStats(edgeSamples / (double)samples, totalDiff / samples);
    }

    private static double CalculateWhiteRatio(
        Mat gray,
        int left,
        int right,
        int top,
        int bottom)
    {
        var clampedLeft = Math.Clamp(left, 0, gray.Width - 1);
        var clampedRight = Math.Clamp(right, clampedLeft + 1, gray.Width);
        var clampedTop = Math.Clamp(top, 0, gray.Height - 1);
        var clampedBottom = Math.Clamp(bottom, clampedTop + 1, gray.Height);
        var whiteCount = 0;
        var samples = 0;

        for (var y = clampedTop; y < clampedBottom; y += 8)
        {
            for (var x = clampedLeft; x < clampedRight; x += 8)
            {
                if (gray.At<byte>(y, x) >= 242)
                {
                    whiteCount++;
                }

                samples++;
            }
        }

        return samples == 0 ? 0 : whiteCount / (double)samples;
    }

    private static int CountMessageLikeRegions(Mat image, int chatLeft, int inputTopY)
    {
        var top = Math.Clamp(CalculateHeaderBottom(inputTopY) + 20, 0, image.Height - 1);
        var bottom = Math.Clamp(inputTopY - 12, top + 1, image.Height);
        var left = Math.Clamp(chatLeft + 16, 0, image.Width - 1);
        var right = Math.Clamp(image.Width - 24, left + 1, image.Width);
        var roi = new CvRect(left, top, right - left, bottom - top);
        if (roi.Width <= 1 || roi.Height <= 1)
        {
            return 0;
        }

        using var crop = new Mat(image, roi);
        using var gray = new Mat();
        using var mask = new Mat();
        Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.InRange(gray, new Scalar(0), new Scalar(210), mask);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(16, 6));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);

        var count = 0;
        var contours = Cv2.FindContoursAsArray(mask, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        foreach (var contour in contours)
        {
            var rect = Cv2.BoundingRect(contour);
            if (rect.Width < 10 ||
                rect.Height < 6 ||
                rect.Height > 90 ||
                rect.Width > roi.Width * 0.72 ||
                rect.Width * rect.Height < 70)
            {
                continue;
            }

            count++;
            if (count >= 10)
            {
                return count;
            }
        }

        return count;
    }

    private static double CalculateSendButtonAlignmentScore(
        int inputTopY,
        int imageHeight,
        DetectedSendButton? sendButton)
    {
        if (sendButton is null)
        {
            return 0;
        }

        var inputHeight = Math.Max(1, imageHeight - inputTopY);
        var buttonCenterY = sendButton.Rect.Top + sendButton.Rect.Height / 2d;
        var normalizedY = (buttonCenterY - inputTopY) / inputHeight;
        var yScore = 1d - Math.Abs(normalizedY - 0.78d) / 0.38d;
        var insideInputScore = sendButton.Rect.Top >= inputTopY && sendButton.Rect.Bottom <= imageHeight
            ? 1d
            : 0d;

        return Math.Clamp(yScore, 0d, 1d) * 0.75d + insideInputScore * 0.25d;
    }

    private static LayoutRegions CreateLayoutRegions(
        DrawingRectangle clientBounds,
        Mat image,
        PanelLayout panelLayout,
        DetectedSendButton? sendButton,
        RpaAutomationOptions options)
    {
        var width = image.Width;
        var height = image.Height;
        var sendButtonRect = sendButton?.Rect;
        var sendButtonLocalPoint = sendButtonRect is null
            ? new CvPoint(
                options.SendButtonPoint.ToScreenPoint(clientBounds).X - clientBounds.Left,
                options.SendButtonPoint.ToScreenPoint(clientBounds).Y - clientBounds.Top)
            : new CvPoint(
                sendButtonRect.Value.X + sendButtonRect.Value.Width / 2,
                sendButtonRect.Value.Y + sendButtonRect.Value.Height / 2);

        var verifyRight = sendButtonRect is null
            ? panelLayout.InputArea.Right - Math.Clamp(panelLayout.InputArea.Width / 10, 96, 180)
            : Math.Max(panelLayout.InputArea.X + 320, sendButtonRect.Value.Left - 18);
        var verifyX = Math.Clamp(panelLayout.InputArea.X + 8, 0, width - 1);
        var verifyTopOffset = Math.Clamp(panelLayout.InputArea.Height / 22, 8, 22);
        var verifyY = Math.Clamp(panelLayout.InputArea.Y + verifyTopOffset, 0, height - 1);
        var maxVerifyWidth = Math.Max(1, Math.Min(width - verifyX, panelLayout.InputArea.Right - verifyX - 8));
        var verifyWidth = Math.Clamp(verifyRight - verifyX, Math.Min(160, maxVerifyWidth), maxVerifyWidth);
        var verifyBottomLimit = sendButtonRect is null
            ? panelLayout.InputArea.Bottom - 48
            : Math.Min(panelLayout.InputArea.Bottom - 44, sendButtonRect.Value.Top - 10);
        if (verifyBottomLimit <= verifyY + 48)
        {
            verifyBottomLimit = panelLayout.InputArea.Bottom - 40;
        }

        var maxVerifyHeight = Math.Max(1, Math.Min(240, panelLayout.InputArea.Bottom - verifyY - 8));
        var minVerifyHeight = Math.Min(64, maxVerifyHeight);
        var verifyHeight = Math.Clamp(verifyBottomLimit - verifyY, minVerifyHeight, maxVerifyHeight);
        var inputVerify = new CvRect(verifyX, verifyY, verifyWidth, verifyHeight);
        var inputClickLocalPoint = new CvPoint(
            Math.Clamp(panelLayout.InputArea.X + 160, panelLayout.InputArea.X + 16, Math.Max(panelLayout.InputArea.X + 16, inputVerify.Right - 20)),
            Math.Clamp(inputVerify.Y + Math.Min(42, Math.Max(16, inputVerify.Height / 2)), panelLayout.InputArea.Y + 8, panelLayout.InputArea.Bottom - 28));

        var incomingX = Math.Clamp(panelLayout.ChatContent.X + 16, 0, width - 1);
        var incomingY = Math.Clamp(panelLayout.ChatContent.Y + 50, 0, height - 1);
        var incomingHeight = Math.Max(100, panelLayout.ChatContent.Bottom - incomingY - 12);
        var incomingWidth = Math.Min(
            Math.Clamp((int)(panelLayout.ChatContent.Width * 0.48), 900, 1800),
            width - incomingX);
        var incomingMessage = new CvRect(incomingX, incomingY, incomingWidth, incomingHeight);
        var contextX = Math.Clamp(panelLayout.ChatContent.X + 16, 0, width - 1);
        var contextY = incomingY;
        var contextRight = Math.Clamp(panelLayout.ChatContent.Right - 16, contextX + 1, width);
        var contextBottom = Math.Clamp(panelLayout.ChatContent.Bottom - 12, contextY + 1, height);
        var conversationContext = new CvRect(contextX, contextY, contextRight - contextX, contextBottom - contextY);

        return new LayoutRegions(
            incomingMessage,
            conversationContext,
            inputClickLocalPoint,
            inputVerify,
            sendButtonLocalPoint,
            panelLayout.ConversationList,
            panelLayout.ChatContent,
            panelLayout.InputArea);
    }

    private static WeChatLayoutCandidateGeometry CreateCandidateGeometry(Mat image, LayoutRegions regions)
    {
        return new WeChatLayoutCandidateGeometry(
            image.Width,
            image.Height,
            regions.ChatContent.Top,
            regions.ChatContent.Bottom,
            regions.InputArea.Top,
            regions.InputArea.Bottom,
            regions.ConversationContext.Top,
            regions.ConversationContext.Bottom,
            regions.InputVerify.Top,
            regions.InputVerify.Bottom,
            Contains(regions.InputArea, regions.InputVerify),
            Contains(regions.ChatContent, regions.ConversationContext),
            regions.ChatContent.Bottom <= regions.InputArea.Top);
    }

    private static WeChatLayoutResult CreateLayoutResult(
        DrawingRectangle clientBounds,
        DetectedX chatLeft,
        DetectedY inputTop,
        PanelLayout panelLayout,
        LayoutRegions regions,
        DetectedSendButton? sendButton,
        WeChatLayoutCandidateScore score,
        int candidateCount)
    {
        var result = new WeChatLayoutResult(
            score.IsSafe,
            score.IsSafe ? "视觉自动定位2.0" : "视觉自动定位2.0失败",
            score.Confidence,
            ToScreenRectangle(clientBounds, regions.IncomingMessage),
            ToScreenRectangle(clientBounds, regions.ConversationContext),
            new DrawingPoint(clientBounds.Left + regions.InputClickPoint.X, clientBounds.Top + regions.InputClickPoint.Y),
            ToScreenRectangle(clientBounds, regions.InputVerify),
            new DrawingPoint(clientBounds.Left + regions.SendButtonPoint.X, clientBounds.Top + regions.SendButtonPoint.Y),
            ToScreenRectangle(clientBounds, regions.ConversationList),
            ToScreenRectangle(clientBounds, regions.ChatContent),
            ToScreenRectangle(clientBounds, regions.InputArea),
            null,
            $"layout={panelLayout.Source}; candidateCount={candidateCount}; chatLeft={chatLeft.X}({chatLeft.Source}); inputTop={inputTop.Y}({inputTop.Source}); sendButton={Format(sendButton)}; {score.Reason}");

        return result;
    }

    private static bool Contains(CvRect outer, CvRect inner)
    {
        return inner.Width > 0 &&
            inner.Height > 0 &&
            inner.Left >= outer.Left &&
            inner.Top >= outer.Top &&
            inner.Right <= outer.Right &&
            inner.Bottom <= outer.Bottom;
    }

    private static DetectedX DetectChatPaneLeft(Mat image, RpaAutomationOptions options)
    {
        using var gray = new Mat();
        Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);

        var minX = Math.Max(220, image.Width / 10);
        var maxX = Math.Min(560, image.Width / 2);
        var top = 90;
        var bottom = Math.Max(top + 160, image.Height - 260);
        var configuredX = Math.Clamp(options.IncomingMessageRegion.X, Math.Max(280, minX), Math.Min(420, maxX));
        var bestX = configuredX;
        var bestScore = double.MinValue;

        for (var x = minX + 56; x < maxX - 96; x++)
        {
            var leftStats = CalculateColumnBandStats(gray, x - 56, x - 12, top, bottom);
            var rightStats = CalculateColumnBandStats(gray, x + 12, x + 132, top, bottom);
            var edgeScore = CalculateVerticalDifferenceScore(gray, x, top, bottom);
            var score =
                (rightStats.AverageBrightness - leftStats.AverageBrightness) * 1.6d +
                (rightStats.WhiteRatio - leftStats.WhiteRatio) * 80d +
                edgeScore * 0.35d -
                Math.Abs(x - configuredX) * 0.04d;

            if (rightStats.WhiteRatio < 0.58d)
            {
                score -= 25d;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestX = x;
            }
        }

        return bestScore > 18d
            ? new DetectedX(bestX, "LayoutSplit")
            : new DetectedX(configuredX, "Fallback");
    }

    private static DetectedY DetectInputTop(Mat image, int chatLeft, DetectedSendButton? sendButton)
    {
        using var gray = new Mat();
        Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);

        // 4K / 高 DPI 最大化窗口下，微信底部输入区可能高于底部 360px。
        // 搜索起点需要更早一些，否则会错过聊天区与输入区之间的分割线，进而退回旧手动坐标。
        var startY = Math.Max((int)(image.Height * 0.52), image.Height - 560);
        var endY = Math.Min(image.Height - 90, image.Height - 80);
        var startX = Math.Clamp(chatLeft + 10, 0, image.Width - 1);
        var endX = Math.Min(image.Width - 1, Math.Max(startX + 100, image.Width - 30));

        var bestY = 0;
        var bestScore = 0d;
        for (var y = startY + 1; y < endY; y++)
        {
            var score = 0d;
            var samples = 0;
            for (var x = startX; x < endX; x += 8)
            {
                score += Math.Abs(gray.At<byte>(y, x) - gray.At<byte>(y - 1, x));
                samples++;
            }

            var averageScore = samples == 0 ? 0 : score / samples;
            if (averageScore > bestScore)
            {
                bestScore = averageScore;
                bestY = y;
            }
        }

        if (bestY > 0 && bestScore >= 3.2d)
        {
            return new DetectedY(bestY, "Visual");
        }

        if (sendButton is not null)
        {
            var inferredFromButton = Math.Clamp(sendButton.Rect.Top - 170, startY, image.Height - 120);
            return new DetectedY(inferredFromButton, "SendButton");
        }

        return new DetectedY(Math.Max(startY, image.Height - 220), "Fallback");
    }

    private static PanelLayout CreatePanelLayout(Mat image, DetectedX chatLeft, DetectedY inputTop)
    {
        var conversationList = WeChatConversationListRegionPlanner.CreateLocalRegion(image.Width, image.Height, chatLeft.X);
        var headerBottom = CalculateHeaderBottom(inputTop.Y);
        var chatWidth = Math.Max(1, image.Width - chatLeft.X);
        var chatHeight = Math.Max(120, inputTop.Y - headerBottom);
        var inputHeight = Math.Max(120, image.Height - inputTop.Y);

        return new PanelLayout(
            new CvRect(conversationList.X, conversationList.Y, conversationList.Width, conversationList.Height),
            new CvRect(chatLeft.X, headerBottom, chatWidth, chatHeight),
            new CvRect(chatLeft.X, inputTop.Y, chatWidth, inputHeight),
            chatLeft.Source == "LayoutSplit" && !inputTop.Source.Contains("Fallback", StringComparison.OrdinalIgnoreCase)
                ? "PanelSegmentation2.0"
                : "PanelSegmentationFallback");
    }

    private static int CalculateHeaderBottom(int inputTopY)
    {
        return Math.Clamp(78, 56, Math.Max(56, inputTopY - 180));
    }

    private static ColumnBandStats CalculateColumnBandStats(Mat gray, int left, int right, int top, int bottom)
    {
        var clampedLeft = Math.Clamp(left, 0, gray.Width - 1);
        var clampedRight = Math.Clamp(right, clampedLeft + 1, gray.Width);
        var clampedTop = Math.Clamp(top, 0, gray.Height - 1);
        var clampedBottom = Math.Clamp(bottom, clampedTop + 1, gray.Height);
        var brightness = 0d;
        var whiteCount = 0;
        var samples = 0;

        for (var y = clampedTop; y < clampedBottom; y += 8)
        {
            for (var x = clampedLeft; x < clampedRight; x += 8)
            {
                var value = gray.At<byte>(y, x);
                brightness += value;
                if (value >= 242)
                {
                    whiteCount++;
                }

                samples++;
            }
        }

        return samples == 0
            ? new ColumnBandStats(0, 0)
            : new ColumnBandStats(brightness / samples, whiteCount / (double)samples);
    }

    private static double CalculateVerticalDifferenceScore(Mat gray, int x, int top, int bottom)
    {
        var clampedX = Math.Clamp(x, 1, gray.Width - 2);
        var clampedTop = Math.Clamp(top, 0, gray.Height - 1);
        var clampedBottom = Math.Clamp(bottom, clampedTop + 1, gray.Height);
        var score = 0d;
        var samples = 0;

        for (var y = clampedTop; y < clampedBottom; y += 4)
        {
            score += Math.Abs(gray.At<byte>(y, clampedX + 1) - gray.At<byte>(y, clampedX - 1));
            samples++;
        }

        return samples == 0 ? 0 : score / samples;
    }

    private static async Task<DetectedSendButton?> DetectSendButtonAsync(
        Mat image,
        byte[] imageBytes,
        int chatLeft,
        CancellationToken cancellationToken)
    {
        return DetectGreenSendButton(image, chatLeft)
            ?? await DetectSendButtonTextAsync(imageBytes, chatLeft, image.Width, image.Height, cancellationToken)
            ?? DetectButtonRectangle(image, chatLeft);
    }

    private static DetectedSendButton? DetectGreenSendButton(Mat image, int chatLeft)
    {
        var roiTop = Math.Max((int)(image.Height * 0.68), image.Height - 360);
        var roi = new CvRect(
            Math.Max(chatLeft, image.Width / 2),
            roiTop,
            Math.Max(1, image.Width - Math.Max(chatLeft, image.Width / 2)),
            image.Height - roiTop);

        using var bottom = new Mat(image, roi);
        using var hsv = new Mat();
        Cv2.CvtColor(bottom, hsv, ColorConversionCodes.BGR2HSV);
        using var mask = new Mat();
        Cv2.InRange(hsv, new Scalar(35, 55, 80), new Scalar(95, 255, 255), mask);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(5, 5));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);

        var contours = Cv2.FindContoursAsArray(mask, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        CvRect? best = null;
        var bestScore = 0d;
        foreach (var contour in contours)
        {
            var rect = Cv2.BoundingRect(contour);
            var global = new CvRect(rect.X + roi.X, rect.Y + roi.Y, rect.Width, rect.Height);
            if (global.Width < 34 || global.Width > 150 || global.Height < 20 || global.Height > 70)
            {
                continue;
            }

            if (global.Right < image.Width * 0.70 || global.Bottom < image.Height * 0.78)
            {
                continue;
            }

            var score = global.Width * global.Height + global.X * 0.2d + global.Y * 0.1d;
            if (score > bestScore)
            {
                bestScore = score;
                best = global;
            }
        }

        return best is null ? null : new DetectedSendButton(best.Value, "GreenButton", 0.40m);
    }

    private static async Task<DetectedSendButton?> DetectSendButtonTextAsync(
        byte[] imageBytes,
        int chatLeft,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(imageBytes.AsBuffer());
            stream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine is null)
            {
                return null;
            }

            var result = await engine.RecognizeAsync(bitmap);
            cancellationToken.ThrowIfCancellationRequested();

            CvRect? best = null;
            var bestScore = 0d;
            var minRight = Math.Max(chatLeft + 360, (int)(width * 0.62));
            var minBottom = (int)(height * 0.68);
            foreach (var word in result.Lines.SelectMany(line => line.Words))
            {
                if (!LooksLikeSendText(word.Text) ||
                    word.BoundingRect.Right < minRight ||
                    word.BoundingRect.Bottom < minBottom)
                {
                    continue;
                }

                var rect = ExpandTextRectToButton(word.BoundingRect, width, height);
                var score = rect.X * 0.35d + rect.Y * 0.45d + rect.Width * rect.Height;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = rect;
                }
            }

            return best is null ? null : new DetectedSendButton(best.Value, "OcrSendText", 0.36m);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static DetectedSendButton? DetectButtonRectangle(Mat image, int chatLeft)
    {
        var roiTop = Math.Max((int)(image.Height * 0.70), image.Height - 280);
        var roiLeft = Math.Max(chatLeft + 360, (int)(image.Width * 0.62));
        if (roiLeft >= image.Width - 80 || roiTop >= image.Height - 80)
        {
            return null;
        }

        var roi = new CvRect(
            roiLeft,
            roiTop,
            image.Width - roiLeft,
            image.Height - roiTop);

        using var bottom = new Mat(image, roi);
        using var gray = new Mat();
        Cv2.CvtColor(bottom, gray, ColorConversionCodes.BGR2GRAY);
        using var edges = new Mat();
        Cv2.Canny(gray, edges, 35, 110);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(5, 3));
        Cv2.MorphologyEx(edges, edges, MorphTypes.Close, kernel);

        var contours = Cv2.FindContoursAsArray(edges, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        CvRect? best = null;
        var bestScore = 0d;
        foreach (var contour in contours)
        {
            var rect = Cv2.BoundingRect(contour);
            var global = new CvRect(rect.X + roi.X, rect.Y + roi.Y, rect.Width, rect.Height);
            var aspectRatio = global.Width / (double)Math.Max(1, global.Height);
            if (global.Width < 48 || global.Width > 160 ||
                global.Height < 22 || global.Height > 62 ||
                aspectRatio < 1.7d || aspectRatio > 4.8d)
            {
                continue;
            }

            if (global.Right < image.Width * 0.72 || global.Bottom < image.Height * 0.76)
            {
                continue;
            }

            var aspectScore = 100d - Math.Abs(aspectRatio - 2.6d) * 20d;
            var score = global.Width * global.Height + global.X * 0.3d + global.Y * 0.3d + aspectScore;
            if (score > bestScore)
            {
                bestScore = score;
                best = global;
            }
        }

        return best is null ? null : new DetectedSendButton(best.Value, "ButtonRectangle", 0.30m);
    }

    private static DetectedSendButton? InferSendButtonFromInputTop(Mat image, DetectedY inputTop)
    {
        if (inputTop.Source == "Fallback")
        {
            return null;
        }

        var buttonWidth = Math.Clamp(image.Width / 24, 72, 118);
        var buttonHeight = 36;
        var x = Math.Clamp(image.Width - buttonWidth - 28, 0, image.Width - buttonWidth);
        var y = Math.Clamp(image.Height - buttonHeight - 26, inputTop.Y + 32, image.Height - buttonHeight);
        return new DetectedSendButton(new CvRect(x, y, buttonWidth, buttonHeight), "InputTopInference", 0.20m);
    }

    private static bool LooksLikeSendText(string? value)
    {
        var compact = new string((value ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());
        return compact.Contains("发送", StringComparison.Ordinal) ||
            compact.Contains("發送", StringComparison.Ordinal);
    }

    private static CvRect ExpandTextRectToButton(Windows.Foundation.Rect textRect, int imageWidth, int imageHeight)
    {
        var centerX = (int)Math.Round(textRect.X + textRect.Width / 2);
        var centerY = (int)Math.Round(textRect.Y + textRect.Height / 2);
        var width = Math.Clamp((int)Math.Round(textRect.Width + 56), 64, 128);
        var height = Math.Clamp((int)Math.Round(textRect.Height + 24), 30, 56);
        var x = Math.Clamp(centerX - width / 2, 0, Math.Max(0, imageWidth - width));
        var y = Math.Clamp(centerY - height / 2, 0, Math.Max(0, imageHeight - height));
        return new CvRect(x, y, width, height);
    }

    private static WeChatLayoutResult? TryCreateManualLayout(DrawingRectangle clientBounds, RpaAutomationOptions options)
    {
            var result = new WeChatLayoutResult(
            true,
            "配置坐标兜底",
            0.50m,
            options.IncomingMessageRegion.ToScreenRectangle(clientBounds),
            options.IncomingMessageRegion.ToScreenRectangle(clientBounds),
            options.InputClickPoint.ToScreenPoint(clientBounds),
            options.InputVerifyRegion.ToScreenRectangle(clientBounds),
            options.SendButtonPoint.ToScreenPoint(clientBounds),
            DrawingRectangle.Empty,
            DrawingRectangle.Empty,
            DrawingRectangle.Empty,
            null,
            "使用 appsettings.json 中的手动坐标。");

        return result.HasValidGeometry(clientBounds) ? result : null;
    }

    private static WeChatLayoutResult FallbackOrFailed(WeChatLayoutResult? manualLayout, string reason)
    {
        return manualLayout is null
            ? WeChatLayoutResult.Failed($"{reason} 配置兜底坐标也无效。")
            : manualLayout.WithMode("配置坐标兜底", reason);
    }

    private static DrawingRectangle ToScreenRectangle(DrawingRectangle clientBounds, CvRect rect)
    {
        return new DrawingRectangle(
            clientBounds.Left + rect.X,
            clientBounds.Top + rect.Y,
            Math.Min(rect.Width, clientBounds.Width - rect.X),
            Math.Min(rect.Height, clientBounds.Height - rect.Y));
    }

    private static string? SaveLayoutDebugCapture(
        Mat image,
        WeChatLayoutResult layout,
        DrawingRectangle clientBounds,
        RpaAutomationOptions options,
        Guid taskId)
    {
        if (!options.EnableLayoutDebugCaptures)
        {
            return null;
        }

        try
        {
            var directory = string.IsNullOrWhiteSpace(options.LayoutDebugCaptureDirectory)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AIChat",
                    "RpaClient",
                    "layout-captures")
                : Environment.ExpandEnvironmentVariables(options.LayoutDebugCaptureDirectory);

            Directory.CreateDirectory(directory);
            using var annotated = image.Clone();
            DrawCandidateInputTopLines(annotated, layout, clientBounds);
            DrawOptionalRectangle(annotated, layout.ConversationListRegion, clientBounds, new Scalar(0, 0, 255), "conversation list");
            DrawOptionalRectangle(annotated, layout.ChatContentRegion, clientBounds, new Scalar(255, 180, 0), "chat content");
            DrawOptionalRectangle(annotated, layout.InputAreaRegion, clientBounds, new Scalar(0, 200, 255), "input area");
            DrawRectangle(annotated, layout.ConversationContextRegion, clientBounds, new Scalar(120, 80, 255), "ocr context");
            DrawRectangle(annotated, layout.IncomingMessageRegion, clientBounds, new Scalar(255, 60, 60), "ocr messages");
            DrawRectangle(annotated, layout.InputVerifyRegion, clientBounds, new Scalar(0, 150, 255), "input verify");
            DrawPoint(annotated, layout.InputClickPoint, clientBounds, new Scalar(255, 0, 255), "input");
            DrawPoint(annotated, layout.SendButtonPoint, clientBounds, new Scalar(0, 220, 0), "send");
            Cv2.PutText(
                annotated,
                $"{layout.Mode} {layout.Confidence:0.00}",
                new CvPoint(20, 32),
                HersheyFonts.HersheySimplex,
                0.8,
                new Scalar(0, 0, 255),
                2);

            var fileName = $"{DateTime.Now:yyyyMMdd-HHmmssfff}-{taskId:N}-layout.png";
            var path = Path.Combine(directory, fileName);
            Cv2.ImWrite(path, annotated);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static void DrawCandidateInputTopLines(Mat image, WeChatLayoutResult layout, DrawingRectangle clientBounds)
    {
        if (layout.DebugCandidates.Count == 0)
        {
            return;
        }

        var startX = layout.ChatContentRegion.Width > 0
            ? Math.Clamp(layout.ChatContentRegion.Left - clientBounds.Left, 0, image.Width - 1)
            : 0;
        var endX = Math.Max(startX + 1, image.Width - 24);
        var index = 0;
        foreach (var candidate in layout.DebugCandidates.Take(8))
        {
            if (candidate.InputTopY <= 0 || candidate.InputTopY >= image.Height)
            {
                continue;
            }

            var color = index == 0
                ? new Scalar(0, 0, 255)
                : candidate.IsSafe
                    ? new Scalar(0, 180, 0)
                    : new Scalar(130, 130, 130);
            Cv2.Line(
                image,
                new CvPoint(startX, candidate.InputTopY),
                new CvPoint(endX, candidate.InputTopY),
                color,
                1);
            Cv2.PutText(
                image,
                $"{index + 1}:{candidate.Source}:{candidate.Confidence:0.00}",
                new CvPoint(startX + 8, Math.Max(20, candidate.InputTopY - 6)),
                HersheyFonts.HersheySimplex,
                0.46,
                color,
                1);
            index++;
        }
    }

    private static void DrawRectangle(Mat image, DrawingRectangle screenRect, DrawingRectangle clientBounds, Scalar color, string label)
    {
        var rect = new CvRect(
            screenRect.Left - clientBounds.Left,
            screenRect.Top - clientBounds.Top,
            screenRect.Width,
            screenRect.Height);
        Cv2.Rectangle(image, rect, color, 2);
        Cv2.PutText(image, label, new CvPoint(rect.X, Math.Max(20, rect.Y - 8)), HersheyFonts.HersheySimplex, 0.55, color, 1);
    }

    private static void DrawOptionalRectangle(Mat image, DrawingRectangle screenRect, DrawingRectangle clientBounds, Scalar color, string label)
    {
        if (screenRect.Width <= 0 || screenRect.Height <= 0)
        {
            return;
        }

        DrawRectangle(image, screenRect, clientBounds, color, label);
    }

    private static void DrawPoint(Mat image, DrawingPoint screenPoint, DrawingRectangle clientBounds, Scalar color, string label)
    {
        var point = new CvPoint(screenPoint.X - clientBounds.Left, screenPoint.Y - clientBounds.Top);
        Cv2.Circle(image, point, 8, color, 2);
        Cv2.PutText(image, label, new CvPoint(point.X + 10, point.Y), HersheyFonts.HersheySimplex, 0.55, color, 1);
    }

    private static string Format(DetectedSendButton? sendButton)
    {
        if (sendButton is null)
        {
            return "none";
        }

        var rect = sendButton.Rect;
        return $"{rect.X},{rect.Y},{rect.Width}x{rect.Height}({sendButton.Source})";
    }

    private static string Format(CvRect? rect)
    {
        return rect is null
            ? "none"
            : $"{rect.Value.X},{rect.Value.Y},{rect.Value.Width}x{rect.Value.Height}";
    }

    private sealed record DetectedX(int X, string Source);
    private sealed record DetectedY(int Y, string Source);
    private sealed record DetectedSendButton(CvRect Rect, string Source, decimal ConfidenceBonus);
    private sealed record ColumnBandStats(double AverageBrightness, double WhiteRatio);
    private sealed record HorizontalBoundaryStats(double HorizontalCoverage, double RowContrast);
    private sealed record InputTopSeed(int Y, string Source, HorizontalBoundaryStats Stats);
    private sealed record PanelLayout(CvRect ConversationList, CvRect ChatContent, CvRect InputArea, string Source);
    private sealed record LayoutRegions(
        CvRect IncomingMessage,
        CvRect ConversationContext,
        CvPoint InputClickPoint,
        CvRect InputVerify,
        CvPoint SendButtonPoint,
        CvRect ConversationList,
        CvRect ChatContent,
        CvRect InputArea);

    private sealed record LayoutCandidateEvaluation(
        WeChatLayoutCandidate Candidate,
        WeChatLayoutCandidateScore Score,
        PanelLayout PanelLayout,
        LayoutRegions Regions,
        DetectedSendButton? SendButton,
        WeChatLayoutResult Result);
}

public sealed record WeChatLayoutResult(
    bool IsUsable,
    string Mode,
    decimal Confidence,
    DrawingRectangle IncomingMessageRegion,
    DrawingRectangle ConversationContextRegion,
    DrawingPoint InputClickPoint,
    DrawingRectangle InputVerifyRegion,
    DrawingPoint SendButtonPoint,
    DrawingRectangle ConversationListRegion,
    DrawingRectangle ChatContentRegion,
    DrawingRectangle InputAreaRegion,
    string? DebugCapturePath,
    string Reason)
{
    public IReadOnlyList<WeChatLayoutDebugCandidate> DebugCandidates { get; init; } = [];

    public static WeChatLayoutResult Failed(string reason)
    {
        return new WeChatLayoutResult(
            false,
            "定位失败",
            0m,
            DrawingRectangle.Empty,
            DrawingRectangle.Empty,
            DrawingPoint.Empty,
            DrawingRectangle.Empty,
            DrawingPoint.Empty,
            DrawingRectangle.Empty,
            DrawingRectangle.Empty,
            DrawingRectangle.Empty,
            null,
            reason);
    }

    public WeChatLayoutResult WithMode(string mode, string reason)
    {
        return this with { Mode = mode, Reason = reason };
    }

    public bool HasValidGeometry(DrawingRectangle clientBounds)
    {
        var basicGeometryIsValid = IsValidRectangle(IncomingMessageRegion, clientBounds) &&
            IsValidRectangle(ConversationContextRegion, clientBounds) &&
            IsValidRectangle(InputVerifyRegion, clientBounds) &&
            clientBounds.Contains(InputClickPoint) &&
            clientBounds.Contains(SendButtonPoint);
        if (!basicGeometryIsValid)
        {
            return false;
        }

        if (ChatContentRegion.IsEmpty || InputAreaRegion.IsEmpty)
        {
            return true;
        }

        return IsValidRectangle(ChatContentRegion, clientBounds) &&
            IsValidRectangle(InputAreaRegion, clientBounds) &&
            ChatContentRegion.Bottom <= InputAreaRegion.Top &&
            !ConversationContextRegion.IntersectsWith(InputAreaRegion) &&
            Contains(InputAreaRegion, InputVerifyRegion) &&
            Contains(ChatContentRegion, ConversationContextRegion);
    }

    public string ToLogMessage()
    {
        var message =
            $"坐标模式：{Mode}，置信度 {Confidence:0.00}，会话列表 {ConversationListRegion}，聊天区 {ChatContentRegion}，输入区 {InputAreaRegion}，OCR客户消息区 {IncomingMessageRegion}，OCR上下文区 {ConversationContextRegion}，输入点击 {InputClickPoint}，输入校验 {InputVerifyRegion}，发送按钮 {SendButtonPoint}。{Reason}";
        return string.IsNullOrWhiteSpace(DebugCapturePath) ? message : $"{message} 布局截图：{DebugCapturePath}";
    }

    private static bool IsValidRectangle(DrawingRectangle rectangle, DrawingRectangle clientBounds)
    {
        return rectangle.Width > 0 &&
            rectangle.Height > 0 &&
            clientBounds.Contains(rectangle.Left, rectangle.Top) &&
            clientBounds.Contains(rectangle.Right - 1, rectangle.Bottom - 1);
    }

    private static bool Contains(DrawingRectangle outer, DrawingRectangle inner)
    {
        return inner.Width > 0 &&
            inner.Height > 0 &&
            inner.Left >= outer.Left &&
            inner.Top >= outer.Top &&
            inner.Right <= outer.Right &&
            inner.Bottom <= outer.Bottom;
    }
}

public sealed record WeChatLayoutDebugCandidate(
    int InputTopY,
    string Source,
    decimal Confidence,
    bool IsSafe,
    string Reason);
