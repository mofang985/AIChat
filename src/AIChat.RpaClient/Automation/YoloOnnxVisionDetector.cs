using System.Drawing;
using System.IO;
using AIChat.RpaClient.Configuration;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using CvPoint = OpenCvSharp.Point;
using CvRect = OpenCvSharp.Rect;

namespace AIChat.RpaClient.Automation;

public sealed class YoloOnnxVisionDetector(ScreenCaptureService screenCaptureService) : IDisposable
{
    private static readonly string[] DefaultLabels =
    [
        "conversation_list",
        "chat_content",
        "input_area",
        "input_box",
        "send_button",
        "customer_message_bubble",
        "self_message_bubble"
    ];

    private readonly object _sessionLock = new();
    private InferenceSession? _session;
    private string? _loadedModelPath;

    public Task<YoloLayoutValidationResult> ValidateAsync(
        WeChatWindow window,
        WeChatLayoutResult opencvLayout,
        RpaAutomationOptions options,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () => Validate(window, opencvLayout, options, taskId, cancellationToken),
            cancellationToken);
    }

    public void Dispose()
    {
        lock (_sessionLock)
        {
            _session?.Dispose();
            _session = null;
            _loadedModelPath = null;
        }
    }

    private YoloLayoutValidationResult Validate(
        WeChatWindow window,
        WeChatLayoutResult opencvLayout,
        RpaAutomationOptions options,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        if (!options.EnableYoloLayoutValidation)
        {
            return YoloLayoutValidationResult.CreateSkipped("YOLO 旁路验证未启用。");
        }

        var modelPath = ResolveConfiguredPath(options.YoloModelPath, "wechat-layout.onnx");
        if (!File.Exists(modelPath))
        {
            return YoloLayoutValidationResult.CreateSkipped($"YOLO 模型不存在，已跳过旁路验证：{modelPath}");
        }

        var labels = LoadLabels(options);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var capture = screenCaptureService.Capture(window.ClientBounds);
            using var image = Cv2.ImDecode(capture.PngBytes, ImreadModes.Color);
            if (image.Empty())
            {
                return YoloLayoutValidationResult.CreateFailed("微信客户区截图为空，YOLO 验证失败。");
            }

            var session = GetOrCreateSession(modelPath);
            var inputName = session.InputMetadata.Keys.First();
            var preprocessed = CreateInputTensor(image, options.YoloInputSize);
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, preprocessed.Tensor)
            };

            using var results = session.Run(inputs);
            cancellationToken.ThrowIfCancellationRequested();

            var output = results
                .Select(result => result.AsTensor<float>())
                .FirstOrDefault(tensor => tensor.Dimensions.ToArray().Length >= 2);

            if (output is null)
            {
                return YoloLayoutValidationResult.CreateFailed("YOLO 模型没有返回可解析的 float tensor。");
            }

            var detections = ParseDetections(
                output,
                labels,
                preprocessed,
                image.Width,
                image.Height,
                options.YoloMinConfidence,
                options.YoloNmsThreshold);

            var debugPath = SaveDebugCapture(
                image,
                window.ClientBounds,
                opencvLayout,
                detections,
                options,
                taskId);

            var summary = FormatSummary(detections);
            return YoloLayoutValidationResult.CreateSucceeded(
                detections,
                detections.Count,
                debugPath,
                $"YOLO 旁路验证完成，命中 {detections.Count} 个区域。{summary}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return YoloLayoutValidationResult.CreateFailed($"YOLO 旁路验证异常：{ex.Message}");
        }
    }

    private InferenceSession GetOrCreateSession(string modelPath)
    {
        lock (_sessionLock)
        {
            if (_session is not null &&
                string.Equals(_loadedModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
            {
                return _session;
            }

            _session?.Dispose();
            using var sessionOptions = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            _session = new InferenceSession(modelPath, sessionOptions);
            _loadedModelPath = modelPath;
            return _session;
        }
    }

    private static YoloPreprocessResult CreateInputTensor(Mat image, int configuredInputSize)
    {
        var inputSize = Math.Clamp(configuredInputSize, 320, 1280);
        var scale = Math.Min(inputSize / (double)image.Width, inputSize / (double)image.Height);
        var resizedWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
        var resizedHeight = Math.Max(1, (int)Math.Round(image.Height * scale));
        var padX = (inputSize - resizedWidth) / 2;
        var padY = (inputSize - resizedHeight) / 2;

        using var resized = new Mat();
        Cv2.Resize(image, resized, new OpenCvSharp.Size(resizedWidth, resizedHeight));
        using var canvas = new Mat(
            new OpenCvSharp.Size(inputSize, inputSize),
            MatType.CV_8UC3,
            new Scalar(114, 114, 114));

        using (var target = new Mat(canvas, new CvRect(padX, padY, resizedWidth, resizedHeight)))
        {
            resized.CopyTo(target);
        }

        var tensor = new DenseTensor<float>([1, 3, inputSize, inputSize]);
        for (var y = 0; y < inputSize; y++)
        {
            for (var x = 0; x < inputSize; x++)
            {
                var pixel = canvas.At<Vec3b>(y, x);
                tensor[0, 0, y, x] = pixel.Item2 / 255f;
                tensor[0, 1, y, x] = pixel.Item1 / 255f;
                tensor[0, 2, y, x] = pixel.Item0 / 255f;
            }
        }

        return new YoloPreprocessResult(tensor, inputSize, scale, padX, padY);
    }

    private static IReadOnlyList<YoloDetection> ParseDetections(
        Tensor<float> output,
        IReadOnlyList<string> labels,
        YoloPreprocessResult preprocess,
        int imageWidth,
        int imageHeight,
        decimal minConfidence,
        decimal nmsThreshold)
    {
        var dimensions = output.Dimensions.ToArray();
        var data = output.ToArray();
        if (dimensions.Length < 2)
        {
            return [];
        }

        var candidates = new List<YoloDetection>();
        if (TryGetYoloMatrixShape(dimensions, out var boxCount, out var attributeCount, out var transposed))
        {
            for (var i = 0; i < boxCount; i++)
            {
                var candidate = ParseYoloCandidate(
                    data,
                    i,
                    boxCount,
                    attributeCount,
                    transposed,
                    labels,
                    preprocess,
                    imageWidth,
                    imageHeight,
                    minConfidence);

                if (candidate is not null)
                {
                    candidates.Add(candidate);
                }
            }
        }

        return ApplyNms(candidates, nmsThreshold);
    }

    private static bool TryGetYoloMatrixShape(
        int[] dimensions,
        out int boxCount,
        out int attributeCount,
        out bool transposed)
    {
        boxCount = 0;
        attributeCount = 0;
        transposed = false;

        if (dimensions.Length == 3)
        {
            var dim1 = dimensions[1];
            var dim2 = dimensions[2];
            transposed = dim1 <= 256 && dim2 > dim1;
            attributeCount = transposed ? dim1 : dim2;
            boxCount = transposed ? dim2 : dim1;
            return boxCount > 0 && attributeCount >= 6;
        }

        if (dimensions.Length == 2)
        {
            boxCount = dimensions[0];
            attributeCount = dimensions[1];
            transposed = false;
            return boxCount > 0 && attributeCount >= 6;
        }

        return false;
    }

    private static YoloDetection? ParseYoloCandidate(
        float[] data,
        int boxIndex,
        int boxCount,
        int attributeCount,
        bool transposed,
        IReadOnlyList<string> labels,
        YoloPreprocessResult preprocess,
        int imageWidth,
        int imageHeight,
        decimal minConfidence)
    {
        float Read(int attributeIndex)
        {
            return transposed
                ? data[(attributeIndex * boxCount) + boxIndex]
                : data[(boxIndex * attributeCount) + attributeIndex];
        }

        if (attributeCount == 6)
        {
            var score = Read(4);
            if (score < (float)minConfidence)
            {
                return null;
            }

            var classId = Math.Max(0, (int)Math.Round(Read(5)));
            var cornerRect = ConvertCornerBox(Read(0), Read(1), Read(2), Read(3), preprocess, imageWidth, imageHeight);
            return cornerRect.Width <= 0 || cornerRect.Height <= 0
                ? null
                : new YoloDetection(GetLabel(labels, classId), (decimal)score, cornerRect);
        }

        var classStart = attributeCount == labels.Count + 5 ? 5 : 4;
        var objectness = classStart == 5 ? Read(4) : 1f;
        var bestClassId = 0;
        var bestClassScore = 0f;
        for (var i = classStart; i < attributeCount; i++)
        {
            var classScore = Read(i);
            if (classScore > bestClassScore)
            {
                bestClassScore = classScore;
                bestClassId = i - classStart;
            }
        }

        var confidence = objectness * bestClassScore;
        if (confidence < (float)minConfidence)
        {
            return null;
        }

        var centerRect = ConvertCenterBox(Read(0), Read(1), Read(2), Read(3), preprocess, imageWidth, imageHeight);
        return centerRect.Width <= 0 || centerRect.Height <= 0
            ? null
            : new YoloDetection(GetLabel(labels, bestClassId), (decimal)confidence, centerRect);
    }

    private static DrawingRectangle ConvertCenterBox(
        float centerX,
        float centerY,
        float width,
        float height,
        YoloPreprocessResult preprocess,
        int imageWidth,
        int imageHeight)
    {
        if (Math.Max(Math.Max(centerX, centerY), Math.Max(width, height)) <= 2f)
        {
            centerX *= preprocess.InputSize;
            centerY *= preprocess.InputSize;
            width *= preprocess.InputSize;
            height *= preprocess.InputSize;
        }

        return ConvertCornerBox(
            centerX - width / 2,
            centerY - height / 2,
            centerX + width / 2,
            centerY + height / 2,
            preprocess,
            imageWidth,
            imageHeight);
    }

    private static DrawingRectangle ConvertCornerBox(
        float x1,
        float y1,
        float x2,
        float y2,
        YoloPreprocessResult preprocess,
        int imageWidth,
        int imageHeight)
    {
        if (Math.Max(Math.Max(x1, y1), Math.Max(x2, y2)) <= 2f)
        {
            x1 *= preprocess.InputSize;
            y1 *= preprocess.InputSize;
            x2 *= preprocess.InputSize;
            y2 *= preprocess.InputSize;
        }

        var left = (x1 - preprocess.PadX) / preprocess.Scale;
        var top = (y1 - preprocess.PadY) / preprocess.Scale;
        var right = (x2 - preprocess.PadX) / preprocess.Scale;
        var bottom = (y2 - preprocess.PadY) / preprocess.Scale;

        var clampedLeft = Math.Clamp((int)Math.Round(left), 0, imageWidth - 1);
        var clampedTop = Math.Clamp((int)Math.Round(top), 0, imageHeight - 1);
        var clampedRight = Math.Clamp((int)Math.Round(right), clampedLeft + 1, imageWidth);
        var clampedBottom = Math.Clamp((int)Math.Round(bottom), clampedTop + 1, imageHeight);

        return DrawingRectangle.FromLTRB(clampedLeft, clampedTop, clampedRight, clampedBottom);
    }

    private static IReadOnlyList<YoloDetection> ApplyNms(
        List<YoloDetection> candidates,
        decimal threshold)
    {
        var result = new List<YoloDetection>();
        var normalizedThreshold = Math.Clamp((double)threshold, 0.05d, 0.95d);
        foreach (var group in candidates.GroupBy(candidate => candidate.Label))
        {
            foreach (var candidate in group.OrderByDescending(candidate => candidate.Confidence))
            {
                if (result
                    .Where(kept => kept.Label == candidate.Label)
                    .All(kept => CalculateIntersectionOverUnion(kept.ClientRectangle, candidate.ClientRectangle) <= normalizedThreshold))
                {
                    result.Add(candidate);
                }
            }
        }

        return result;
    }

    private static double CalculateIntersectionOverUnion(DrawingRectangle first, DrawingRectangle second)
    {
        var intersectionLeft = Math.Max(first.Left, second.Left);
        var intersectionTop = Math.Max(first.Top, second.Top);
        var intersectionRight = Math.Min(first.Right, second.Right);
        var intersectionBottom = Math.Min(first.Bottom, second.Bottom);
        var intersectionWidth = Math.Max(0, intersectionRight - intersectionLeft);
        var intersectionHeight = Math.Max(0, intersectionBottom - intersectionTop);
        var intersectionArea = intersectionWidth * intersectionHeight;
        if (intersectionArea <= 0)
        {
            return 0d;
        }

        var unionArea = first.Width * first.Height + second.Width * second.Height - intersectionArea;
        return unionArea <= 0 ? 0d : intersectionArea / (double)unionArea;
    }

    private static string? SaveDebugCapture(
        Mat image,
        DrawingRectangle clientBounds,
        WeChatLayoutResult opencvLayout,
        IReadOnlyList<YoloDetection> detections,
        RpaAutomationOptions options,
        Guid taskId)
    {
        if (!options.EnableYoloDebugCaptures)
        {
            return null;
        }

        try
        {
            var directory = ResolveDebugDirectory(options);
            Directory.CreateDirectory(directory);
            using var annotated = image.Clone();

            DrawOptionalScreenRectangle(annotated, opencvLayout.ConversationListRegion, clientBounds, new Scalar(255, 120, 0), "opencv conversation");
            DrawOptionalScreenRectangle(annotated, opencvLayout.ChatContentRegion, clientBounds, new Scalar(255, 180, 0), "opencv chat");
            DrawOptionalScreenRectangle(annotated, opencvLayout.InputAreaRegion, clientBounds, new Scalar(0, 180, 255), "opencv input area");
            DrawOptionalScreenRectangle(annotated, opencvLayout.ConversationContextRegion, clientBounds, new Scalar(120, 80, 255), "opencv context");
            DrawOptionalScreenRectangle(annotated, opencvLayout.IncomingMessageRegion, clientBounds, new Scalar(255, 60, 60), "opencv customer ocr");
            DrawOptionalScreenRectangle(annotated, opencvLayout.InputVerifyRegion, clientBounds, new Scalar(0, 150, 255), "opencv verify");
            DrawScreenPoint(annotated, opencvLayout.InputClickPoint, clientBounds, new Scalar(255, 0, 255), "opencv input");
            DrawScreenPoint(annotated, opencvLayout.SendButtonPoint, clientBounds, new Scalar(0, 220, 0), "opencv send");

            foreach (var detection in detections)
            {
                var label = $"yolo {detection.Label} {detection.Confidence:0.00}";
                Cv2.Rectangle(annotated, ToCvRect(detection.ClientRectangle), new Scalar(40, 220, 40), 2);
                Cv2.PutText(
                    annotated,
                    label,
                    new CvPoint(detection.ClientRectangle.X, Math.Max(20, detection.ClientRectangle.Y - 8)),
                    HersheyFonts.HersheySimplex,
                    0.50,
                    new Scalar(40, 220, 40),
                    1);
            }

            Cv2.PutText(
                annotated,
                $"YOLO detections: {detections.Count}",
                new CvPoint(20, 32),
                HersheyFonts.HersheySimplex,
                0.75,
                new Scalar(40, 220, 40),
                2);

            var fileName = $"{DateTime.Now:yyyyMMdd-HHmmssfff}-{taskId:N}-yolo-layout.png";
            var path = Path.Combine(directory, fileName);
            Cv2.ImWrite(path, annotated);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> LoadLabels(RpaAutomationOptions options)
    {
        var labelsPath = ResolveConfiguredPath(options.YoloLabelsPath, "labels.txt");
        if (!File.Exists(labelsPath))
        {
            return DefaultLabels;
        }

        var labels = File.ReadAllLines(labelsPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();

        return labels.Length == 0 ? DefaultLabels : labels;
    }

    private static string ResolveConfiguredPath(string? configuredPath, string defaultFileName)
    {
        var rawPath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(GetDefaultModelDirectory(), defaultFileName)
            : Environment.ExpandEnvironmentVariables(configuredPath);

        return Path.IsPathRooted(rawPath)
            ? rawPath
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, rawPath));
    }

    private static string ResolveDebugDirectory(RpaAutomationOptions options)
    {
        return string.IsNullOrWhiteSpace(options.YoloDebugCaptureDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AIChat",
                "RpaClient",
                "yolo-captures")
            : Environment.ExpandEnvironmentVariables(options.YoloDebugCaptureDirectory);
    }

    private static string GetDefaultModelDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIChat",
            "RpaClient",
            "models",
            "wechat-layout");
    }

    private static string GetLabel(IReadOnlyList<string> labels, int classId)
    {
        return classId >= 0 && classId < labels.Count ? labels[classId] : $"class_{classId}";
    }

    private static string FormatSummary(IReadOnlyList<YoloDetection> detections)
    {
        if (detections.Count == 0)
        {
            return "未命中任何标签。";
        }

        var summary = string.Join(
            "，",
            detections
                .GroupBy(detection => detection.Label)
                .OrderBy(group => group.Key)
                .Select(group => $"{group.Key}:{group.Count()}"));
        return $"标签统计：{summary}。";
    }

    private static void DrawOptionalScreenRectangle(
        Mat image,
        DrawingRectangle screenRect,
        DrawingRectangle clientBounds,
        Scalar color,
        string label)
    {
        if (screenRect.Width <= 0 || screenRect.Height <= 0)
        {
            return;
        }

        var localRect = new DrawingRectangle(
            screenRect.Left - clientBounds.Left,
            screenRect.Top - clientBounds.Top,
            screenRect.Width,
            screenRect.Height);
        Cv2.Rectangle(image, ToCvRect(localRect), color, 2);
        Cv2.PutText(image, label, new CvPoint(localRect.X, Math.Max(20, localRect.Y - 8)), HersheyFonts.HersheySimplex, 0.50, color, 1);
    }

    private static void DrawScreenPoint(
        Mat image,
        DrawingPoint screenPoint,
        DrawingRectangle clientBounds,
        Scalar color,
        string label)
    {
        if (screenPoint.IsEmpty || !clientBounds.Contains(screenPoint))
        {
            return;
        }

        var point = new CvPoint(screenPoint.X - clientBounds.Left, screenPoint.Y - clientBounds.Top);
        Cv2.Circle(image, point, 8, color, 2);
        Cv2.PutText(image, label, new CvPoint(point.X + 10, point.Y), HersheyFonts.HersheySimplex, 0.50, color, 1);
    }

    private static CvRect ToCvRect(DrawingRectangle rectangle)
    {
        return new CvRect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
    }

    private sealed record YoloPreprocessResult(
        DenseTensor<float> Tensor,
        int InputSize,
        double Scale,
        int PadX,
        int PadY);
}

public sealed record YoloDetection(
    string Label,
    decimal Confidence,
    DrawingRectangle ClientRectangle);

public sealed record YoloLayoutValidationResult(
    bool IsEnabled,
    bool Succeeded,
    bool Skipped,
    string Status,
    int DetectionCount,
    IReadOnlyList<YoloDetection> Detections,
    string? DebugCapturePath,
    string Message)
{
    public static YoloLayoutValidationResult CreateSkipped(string message)
    {
        return new YoloLayoutValidationResult(false, false, true, "YOLO skipped", 0, [], null, message);
    }

    public static YoloLayoutValidationResult CreateFailed(string message)
    {
        return new YoloLayoutValidationResult(true, false, false, "YOLO failed", 0, [], null, message);
    }

    public static YoloLayoutValidationResult CreateSucceeded(IReadOnlyList<YoloDetection> detections, int detectionCount, string? debugCapturePath, string message)
    {
        return new YoloLayoutValidationResult(true, true, false, "YOLO validated", detectionCount, detections, debugCapturePath, message);
    }

    public string ToLogMessage()
    {
        return string.IsNullOrWhiteSpace(DebugCapturePath)
            ? Message
            : $"{Message} 调试截图：{DebugCapturePath}";
    }
}
