using System.Drawing;
using System.IO;
using AIChat.RpaClient.Configuration;
using OpenCvSharp;
using DrawingRectangle = System.Drawing.Rectangle;
using CvRect = OpenCvSharp.Rect;

namespace AIChat.RpaClient.Automation;

public sealed class UnreadConversationQueueScanner(ScreenCaptureService screenCaptureService, PaddleOcrEngine ocrEngine)
{
    private readonly UnreadConversationQueueStabilityTracker _stabilityTracker = new();
    public async Task<UnreadConversationQueueSnapshot> ScanAsync(
        WeChatLayoutResult layout,
        RpaAutomationOptions options,
        Guid scanId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var region = layout.ConversationListRegion;
        if (region.IsEmpty || region.Width <= 0 || region.Height <= 0)
        {
            return UnreadConversationQueueSnapshot.Empty(DateTimeOffset.UtcNow, region, "会话列表区域为空，无法扫描未读队列。");
        }

        using var capture = screenCaptureService.Capture(region);
        using var image = Cv2.ImDecode(capture.PngBytes, ImreadModes.Color);
        if (image.Empty())
        {
            return UnreadConversationQueueSnapshot.Empty(DateTimeOffset.UtcNow, region, "会话列表截图为空，无法扫描未读队列。");
        }

        var detections = DetectUnreadBadges(image, region, options).ToArray();
        var candidates = UnreadConversationQueueAnalyzer.BuildQueue(
            region,
            detections,
            Math.Max(0, options.MaxUnreadQueueCandidates));
        var enrichedCandidates = await EnrichCandidatesWithOcrAsync(image, region, candidates, cancellationToken);
        var textInfoCount = enrichedCandidates.Count(candidate => candidate.TextInfo is { HasAnyText: true });
        var summary = enrichedCandidates.Count == 0
            ? $"未识别到可见数字未读会话候选，数字角标候选 {detections.Length} 个。"
            : $"识别到 {enrichedCandidates.Count} 个可见数字未读会话候选，已读取 {textInfoCount} 个候选行文本，数字角标候选 {detections.Length} 个。";
        var snapshot = new UnreadConversationQueueSnapshot(DateTimeOffset.UtcNow, region, enrichedCandidates, summary, null);
        snapshot = _stabilityTracker.Apply(snapshot, options);
        var debugPath = options.EnableUnreadQueueDebugCaptures
            ? SaveDebugCapture(image, region, snapshot.Candidates, options, scanId)
            : null;

        return snapshot with { DebugCapturePath = debugPath };
    }

    private static IEnumerable<UnreadBadgeDetection> DetectUnreadBadges(Mat image, DrawingRectangle screenRegion, RpaAutomationOptions options)
    {
        using var hsv = new Mat();
        Cv2.CvtColor(image, hsv, ColorConversionCodes.BGR2HSV);

        using var lowerRed = new Mat();
        using var upperRed = new Mat();
        using var mask = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 80, 90), new Scalar(12, 255, 255), lowerRed);
        Cv2.InRange(hsv, new Scalar(168, 70, 90), new Scalar(180, 255, 255), upperRed);
        Cv2.BitwiseOr(lowerRed, upperRed, mask);

        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(3, 3));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Open, kernel);
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);

        Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        foreach (var contour in contours)
        {
            var rect = Cv2.BoundingRect(contour);
            if (!UnreadConversationListGeometry.LooksLikeUnreadBadgeLocal(new DrawingRectangle(rect.X, rect.Y, rect.Width, rect.Height), image.Width, image.Height))
            {
                continue;
            }

            var area = Math.Max(1, rect.Width * rect.Height);
            var contourArea = Cv2.ContourArea(contour);
            var fillRatio = Math.Clamp(contourArea / area, 0d, 1d);
            if (fillRatio < 0.52d)
            {
                continue;
            }
            var containsNumberGlyph = UnreadNumberGlyphDetector.ContainsNumberGlyph(image, rect);
            if (!containsNumberGlyph)
            {
                continue;
            }


            var confidence = CalculateConfidence(rect, fillRatio, image.Height);
            if (confidence < options.UnreadQueueMinConfidence)
            {
                continue;
            }

            yield return new UnreadBadgeDetection(
                new DrawingRectangle(screenRegion.Left + rect.X, screenRegion.Top + rect.Y, rect.Width, rect.Height),
                confidence,
                "NumberBadgeOpenCv",
                containsNumberGlyph);
        }

    }


    private async Task<IReadOnlyList<UnreadConversationCandidate>> EnrichCandidatesWithOcrAsync(
        Mat image,
        DrawingRectangle screenRegion,
        IReadOnlyList<UnreadConversationCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return candidates;
        }

        var enriched = new List<UnreadConversationCandidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var textRegion = UnreadConversationRowOcrPlanner.CreateTextRegion(candidate.RowBounds);
            var rowResult = await RecognizeRegionAsync(image, screenRegion, textRegion, cancellationToken);
            var textInfo = UnreadConversationRowOcrParser.BuildInfoFromRow(rowResult);
            enriched.Add(candidate with { TextInfo = textInfo });
        }

        return enriched;
    }

    private async Task<OcrResult> RecognizeRegionAsync(
        Mat image,
        DrawingRectangle screenRegion,
        DrawingRectangle region,
        CancellationToken cancellationToken)
    {
        if (region.IsEmpty || region.Width <= 0 || region.Height <= 0)
        {
            return new OcrResult(string.Empty, 0m, "UnreadRowOcrEmptyRegion");
        }

        var localRect = ToLocalRect(region, screenRegion);
        var safeRect = IntersectImage(localRect, image.Width, image.Height);
        if (safeRect.Width <= 0 || safeRect.Height <= 0)
        {
            return new OcrResult(string.Empty, 0m, "UnreadRowOcrOutOfBounds");
        }

        using var crop = new Mat(image, safeRect);
        using var prepared = PrepareOcrCrop(crop);
        if (!Cv2.ImEncode(".png", prepared, out var pngBytes))
        {
            return new OcrResult(string.Empty, 0m, "UnreadRowOcrEncodeFailed");
        }

        var result = await ocrEngine.RecognizeUiTextAsync(pngBytes, cancellationToken);
        return result with { Source = $"UnreadRowOcr:{result.Source}" };
    }

    private static Mat PrepareOcrCrop(Mat crop)
    {
        var scale = crop.Height < 48 ? 48d / Math.Max(1, crop.Height) : 1d;
        if (scale <= 1.01d)
        {
            return crop.Clone();
        }

        var resized = new Mat();
        Cv2.Resize(crop, resized, new OpenCvSharp.Size(), scale, scale, InterpolationFlags.Lanczos4);
        return resized;
    }

    private static CvRect IntersectImage(CvRect rect, int imageWidth, int imageHeight)
    {
        var left = Math.Clamp(rect.Left, 0, imageWidth);
        var top = Math.Clamp(rect.Top, 0, imageHeight);
        var right = Math.Clamp(rect.Right, left, imageWidth);
        var bottom = Math.Clamp(rect.Bottom, top, imageHeight);
        return new CvRect(left, top, right - left, bottom - top);
    }

    private static decimal CalculateConfidence(CvRect rect, double fillRatio, int imageHeight)
    {
        var sizeScore = Math.Clamp(rect.Height / 18d, 0.35d, 1d);
        var rowScore = rect.Y > imageHeight * 0.08d ? 1d : 0.65d;
        var confidence = fillRatio * 0.55d + sizeScore * 0.30d + rowScore * 0.15d;
        return (decimal)Math.Clamp(confidence, 0d, 0.99d);
    }

    private static string SaveDebugCapture(
        Mat image,
        DrawingRectangle screenRegion,
        IReadOnlyList<UnreadConversationCandidate> candidates,
        RpaAutomationOptions options,
        Guid scanId)
    {
        var directory = string.IsNullOrWhiteSpace(options.UnreadQueueDebugCaptureDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIChat", "RpaClient", "unread-queue-captures")
            : Environment.ExpandEnvironmentVariables(options.UnreadQueueDebugCaptureDirectory);
        Directory.CreateDirectory(directory);

        using var annotated = image.Clone();
        Cv2.Rectangle(annotated, new CvRect(0, 0, Math.Max(1, image.Width - 1), Math.Max(1, image.Height - 1)), new Scalar(0, 0, 255), 2);
        Cv2.PutText(annotated, "conversation list scan region", new OpenCvSharp.Point(8, 22), HersheyFonts.HersheySimplex, 0.55, new Scalar(0, 0, 255), 1);
        foreach (var candidate in candidates)
        {
            var localBadge = ToLocalRect(candidate.BadgeBounds, screenRegion);
            var localRow = ToLocalRect(candidate.RowBounds, screenRegion);
            Cv2.Rectangle(annotated, localRow, new Scalar(255, 180, 0), 2);
            Cv2.Rectangle(annotated, localBadge, new Scalar(0, 255, 255), 2);
            Cv2.PutText(
                annotated,
                FormatDebugLabel(candidate),
                new OpenCvSharp.Point(Math.Max(0, localBadge.X - 4), Math.Max(16, localBadge.Y - 6)),
                HersheyFonts.HersheySimplex,
                0.45,
                new Scalar(0, 255, 255),
                1);
        }

        var path = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd-HHmmssfff}-{scanId:N}-UnreadQueue.png");
        Cv2.ImWrite(path, annotated);
        return path;
    }

    private static string FormatDebugLabel(UnreadConversationCandidate candidate)
    {
        var status = candidate.Preflight?.StatusText switch
        {
            "可切换候选" => "stable",
            "观察中" => "pending",
            "跳过" => "skip",
            _ => "scan"
        };
        return $"#{candidate.VisualOrder + 1} {candidate.Confidence:0.00} {status}";
    }

    private static CvRect ToLocalRect(DrawingRectangle rectangle, DrawingRectangle screenRegion)
    {
        return new CvRect(
            rectangle.Left - screenRegion.Left,
            rectangle.Top - screenRegion.Top,
            rectangle.Width,
            rectangle.Height);
    }
}
