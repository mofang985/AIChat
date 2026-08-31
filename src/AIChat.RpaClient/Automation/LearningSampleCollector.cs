using System.Drawing;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIChat.RpaClient.Configuration;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;

namespace AIChat.RpaClient.Automation;

public sealed class LearningSampleCollector(ScreenCaptureService screenCaptureService)
{
    private static readonly string[] Labels =
    [
        "conversation_list",
        "chat_content",
        "input_area",
        "input_box",
        "send_button",
        "customer_message_bubble",
        "self_message_bubble"
    ];

    private static readonly Dictionary<string, int> LabelIds = Labels
        .Select((label, index) => new { label, index })
        .ToDictionary(item => item.label, item => item.index, StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<LearningSampleCaptureResult?> CaptureAsync(
        WeChatWindow window,
        WeChatLayoutResult? layout,
        YoloLayoutValidationResult? yoloResult,
        RpaAutomationOptions options,
        Guid? taskId,
        string conversationKey,
        string taskStatus,
        string? failureAction,
        string? failureReason,
        string? incomingMessageText,
        string? aiReplyText,
        CancellationToken cancellationToken)
    {
        if (!options.EnableLearningSampleCapture)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var capture = screenCaptureService.Capture(window.ClientBounds);
        var bucket = SelectBucket(layout, yoloResult, options, taskStatus, failureAction);
        var rootDirectory = ResolveLearningSampleDirectory(options);
        var bucketDirectory = Path.Combine(rootDirectory, bucket);
        Directory.CreateDirectory(bucketDirectory);
        CleanupExpiredSamples(rootDirectory, options.LearningSampleRetentionDays);

        var capturedAt = DateTimeOffset.Now;
        var safeTaskId = taskId?.ToString("N") ?? Guid.NewGuid().ToString("N");
        var imageHash = Convert.ToHexString(SHA256.HashData(capture.PngBytes)).ToLowerInvariant();
        var baseName = $"{capturedAt:yyyyMMdd-HHmmssfff}-{safeTaskId}-learning";
        var imagePath = Path.Combine(bucketDirectory, $"{baseName}.png");
        var labelPath = Path.Combine(bucketDirectory, $"{baseName}.txt");
        var metadataPath = Path.Combine(bucketDirectory, $"{baseName}.json");

        await File.WriteAllBytesAsync(imagePath, capture.PngBytes, cancellationToken);

        var draftBoxes = BuildDraftBoxes(window.ClientBounds, layout, yoloResult);
        var yoloText = string.Join(Environment.NewLine, draftBoxes.Select(box => ToYoloLine(box, window.ClientBounds.Size)));
        if (yoloText.Length > 0)
        {
            yoloText += Environment.NewLine;
        }

        await File.WriteAllTextAsync(labelPath, yoloText, Encoding.UTF8, cancellationToken);

        var metadata = CreateMetadata(
            window,
            layout,
            yoloResult,
            options,
            taskId,
            conversationKey,
            taskStatus,
            failureAction,
            failureReason,
            bucket,
            capturedAt,
            imageHash,
            imagePath,
            labelPath,
            draftBoxes,
            incomingMessageText,
            aiReplyText);

        await File.WriteAllTextAsync(
            metadataPath,
            JsonSerializer.Serialize(metadata, JsonOptions),
            Encoding.UTF8,
            cancellationToken);

        return new LearningSampleCaptureResult(bucket, imagePath, metadataPath, labelPath, draftBoxes.Count);
    }

    private static string SelectBucket(
        WeChatLayoutResult? layout,
        YoloLayoutValidationResult? yoloResult,
        RpaAutomationOptions options,
        string taskStatus,
        string? failureAction)
    {
        var succeeded = taskStatus.Equals("Succeeded", StringComparison.OrdinalIgnoreCase);
        var lowLayoutConfidence = layout is null || !layout.IsUsable || layout.Confidence < options.LearningSampleMinReviewConfidence;
        var yoloAbnormal = yoloResult is { IsEnabled: true, Succeeded: false, Skipped: false };
        var inputVerifyFailed = failureAction?.Equals("InputVerifyFailed", StringComparison.OrdinalIgnoreCase) == true ||
            failureAction?.Equals("SendVerifyFailed", StringComparison.OrdinalIgnoreCase) == true;

        return succeeded && !lowLayoutConfidence && !yoloAbnormal && !inputVerifyFailed
            ? "accepted"
            : "review";
    }

    private static List<DraftBox> BuildDraftBoxes(
        DrawingRectangle clientBounds,
        WeChatLayoutResult? layout,
        YoloLayoutValidationResult? yoloResult)
    {
        var boxes = new List<DraftBox>();
        var detections = yoloResult?.Detections ?? [];

        AddPrimaryBox(boxes, "conversation_list", TryLocalRectangle(layout?.ConversationListRegion, clientBounds), detections);
        AddPrimaryBox(boxes, "chat_content", TryLocalRectangle(layout?.ChatContentRegion, clientBounds), detections);
        AddPrimaryBox(boxes, "input_area", TryLocalRectangle(layout?.InputAreaRegion, clientBounds), detections);
        AddPrimaryBox(boxes, "input_box", TryLocalRectangle(layout?.InputVerifyRegion, clientBounds), detections);

        var yoloSendButton = FindBestDetection(detections, "send_button")?.ClientRectangle;
        var fallbackSendButton = TryCreateCenteredLocalRectangle(layout?.SendButtonPoint, clientBounds, 118, 56);
        AddPrimaryBox(boxes, "send_button", yoloSendButton ?? fallbackSendButton, detections, preferExisting: yoloSendButton is not null);

        foreach (var detection in detections.Where(detection =>
                     detection.Label.Equals("customer_message_bubble", StringComparison.OrdinalIgnoreCase) ||
                     detection.Label.Equals("self_message_bubble", StringComparison.OrdinalIgnoreCase)))
        {
            if (TryClampLocalRectangle(detection.ClientRectangle, clientBounds.Size, out var localRect))
            {
                boxes.Add(new DraftBox(detection.Label, localRect, detection.Confidence, "Yolo"));
            }
        }

        return boxes
            .Where(box => LabelIds.ContainsKey(box.Label))
            .OrderBy(box => LabelIds[box.Label])
            .ThenBy(box => box.LocalRectangle.Top)
            .ThenBy(box => box.LocalRectangle.Left)
            .ToList();
    }

    private static void AddPrimaryBox(
        List<DraftBox> boxes,
        string label,
        DrawingRectangle? localRect,
        IReadOnlyList<YoloDetection> detections,
        bool preferExisting = false)
    {
        var selectedRect = localRect;
        var source = preferExisting ? "Yolo" : "OpenCv";
        var confidence = preferExisting ? FindBestDetection(detections, label)?.Confidence ?? 0m : 1m;

        if (selectedRect is null)
        {
            var detection = FindBestDetection(detections, label);
            selectedRect = detection?.ClientRectangle;
            source = "Yolo";
            confidence = detection?.Confidence ?? 0m;
        }

        if (selectedRect is not null)
        {
            boxes.Add(new DraftBox(label, selectedRect.Value, confidence, source));
        }
    }

    private static YoloDetection? FindBestDetection(IReadOnlyList<YoloDetection> detections, string label)
    {
        return detections
            .Where(detection => detection.Label.Equals(label, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(detection => detection.Confidence)
            .FirstOrDefault();
    }

    private static DrawingRectangle? TryLocalRectangle(DrawingRectangle? screenRectangle, DrawingRectangle clientBounds)
    {
        if (screenRectangle is null || screenRectangle.Value.Width <= 0 || screenRectangle.Value.Height <= 0)
        {
            return null;
        }

        var local = new DrawingRectangle(
            screenRectangle.Value.Left - clientBounds.Left,
            screenRectangle.Value.Top - clientBounds.Top,
            screenRectangle.Value.Width,
            screenRectangle.Value.Height);

        return TryClampLocalRectangle(local, clientBounds.Size, out var clamped) ? clamped : null;
    }

    private static DrawingRectangle? TryCreateCenteredLocalRectangle(
        DrawingPoint? screenPoint,
        DrawingRectangle clientBounds,
        int width,
        int height)
    {
        if (screenPoint is null || screenPoint.Value.IsEmpty || !clientBounds.Contains(screenPoint.Value))
        {
            return null;
        }

        var local = new DrawingRectangle(
            screenPoint.Value.X - clientBounds.Left - width / 2,
            screenPoint.Value.Y - clientBounds.Top - height / 2,
            width,
            height);

        return TryClampLocalRectangle(local, clientBounds.Size, out var clamped) ? clamped : null;
    }

    private static bool TryClampLocalRectangle(DrawingRectangle rectangle, Size imageSize, out DrawingRectangle clamped)
    {
        var left = Math.Clamp(rectangle.Left, 0, Math.Max(0, imageSize.Width - 1));
        var top = Math.Clamp(rectangle.Top, 0, Math.Max(0, imageSize.Height - 1));
        var right = Math.Clamp(rectangle.Right, left + 1, imageSize.Width);
        var bottom = Math.Clamp(rectangle.Bottom, top + 1, imageSize.Height);
        clamped = DrawingRectangle.FromLTRB(left, top, right, bottom);
        return clamped.Width > 0 && clamped.Height > 0;
    }

    private static string ToYoloLine(DraftBox box, Size imageSize)
    {
        var classId = LabelIds[box.Label];
        var xCenter = (box.LocalRectangle.Left + box.LocalRectangle.Width / 2d) / imageSize.Width;
        var yCenter = (box.LocalRectangle.Top + box.LocalRectangle.Height / 2d) / imageSize.Height;
        var width = box.LocalRectangle.Width / (double)imageSize.Width;
        var height = box.LocalRectangle.Height / (double)imageSize.Height;
        return string.Join(
            " ",
            classId.ToString(CultureInfo.InvariantCulture),
            xCenter.ToString("0.######", CultureInfo.InvariantCulture),
            yCenter.ToString("0.######", CultureInfo.InvariantCulture),
            width.ToString("0.######", CultureInfo.InvariantCulture),
            height.ToString("0.######", CultureInfo.InvariantCulture));
    }

    private static object CreateMetadata(
        WeChatWindow window,
        WeChatLayoutResult? layout,
        YoloLayoutValidationResult? yoloResult,
        RpaAutomationOptions options,
        Guid? taskId,
        string conversationKey,
        string taskStatus,
        string? failureAction,
        string? failureReason,
        string bucket,
        DateTimeOffset capturedAt,
        string imageHash,
        string imagePath,
        string labelPath,
        IReadOnlyList<DraftBox> draftBoxes,
        string? incomingMessageText,
        string? aiReplyText)
    {
        var modelInfo = ResolveModelInfo(options);
        return new
        {
            SchemaVersion = "m4.3-learning-sample-v1",
            CapturedAt = capturedAt,
            Bucket = bucket,
            TaskId = taskId,
            ConversationKey = conversationKey,
            TaskStatus = taskStatus,
            FailureAction = failureAction,
            FailureReason = failureReason,
            ImageHash = imageHash,
            ImagePath = imagePath,
            LabelPath = labelPath,
            IncludeText = options.IncludeLearningSampleText,
            IncomingMessageText = options.IncludeLearningSampleText ? incomingMessageText : null,
            AiReplyText = options.IncludeLearningSampleText ? aiReplyText : null,
            Window = new
            {
                window.Title,
                ClientBounds = ToRectangleDto(window.ClientBounds)
            },
            Layout = layout is null ? null : new
            {
                layout.IsUsable,
                layout.Mode,
                layout.Confidence,
                layout.Reason,
                layout.DebugCapturePath,
                ConversationListRegion = ToLocalRectangleDto(layout.ConversationListRegion, window.ClientBounds),
                ChatContentRegion = ToLocalRectangleDto(layout.ChatContentRegion, window.ClientBounds),
                InputAreaRegion = ToLocalRectangleDto(layout.InputAreaRegion, window.ClientBounds),
                InputVerifyRegion = ToLocalRectangleDto(layout.InputVerifyRegion, window.ClientBounds),
                IncomingMessageRegion = ToLocalRectangleDto(layout.IncomingMessageRegion, window.ClientBounds),
                ConversationContextRegion = ToLocalRectangleDto(layout.ConversationContextRegion, window.ClientBounds),
                InputClickPoint = ToLocalPointDto(layout.InputClickPoint, window.ClientBounds),
                SendButtonPoint = ToLocalPointDto(layout.SendButtonPoint, window.ClientBounds)
            },
            Yolo = yoloResult is null ? null : new
            {
                yoloResult.IsEnabled,
                yoloResult.Succeeded,
                yoloResult.Skipped,
                yoloResult.Status,
                yoloResult.DetectionCount,
                yoloResult.DebugCapturePath,
                yoloResult.Message,
                ModelPath = modelInfo.ModelPath,
                ModelVersion = modelInfo.ModelVersion,
                Detections = yoloResult.Detections.Select(detection => new
                {
                    detection.Label,
                    detection.Confidence,
                    Rectangle = ToRectangleDto(detection.ClientRectangle)
                }).ToArray()
            },
            DraftLabels = draftBoxes.Select(box => new
            {
                box.Label,
                box.Confidence,
                box.Source,
                Rectangle = ToRectangleDto(box.LocalRectangle)
            }).ToArray()
        };
    }

    private static object? ToLocalRectangleDto(DrawingRectangle screenRectangle, DrawingRectangle clientBounds)
    {
        var local = TryLocalRectangle(screenRectangle, clientBounds);
        return local is null ? null : ToRectangleDto(local.Value);
    }

    private static object? ToLocalPointDto(DrawingPoint screenPoint, DrawingRectangle clientBounds)
    {
        if (screenPoint.IsEmpty || !clientBounds.Contains(screenPoint))
        {
            return null;
        }

        return new
        {
            X = screenPoint.X - clientBounds.Left,
            Y = screenPoint.Y - clientBounds.Top
        };
    }

    private static object ToRectangleDto(DrawingRectangle rectangle)
    {
        return new
        {
            rectangle.X,
            rectangle.Y,
            rectangle.Width,
            rectangle.Height
        };
    }

    private static (string ModelPath, string? ModelVersion) ResolveModelInfo(RpaAutomationOptions options)
    {
        var modelPath = ResolveConfiguredPath(options.YoloModelPath, "wechat-layout.onnx");
        var versionPath = Path.Combine(Path.GetDirectoryName(modelPath) ?? string.Empty, "model-version.json");
        if (!File.Exists(versionPath))
        {
            return (modelPath, null);
        }

        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(versionPath, Encoding.UTF8));
            if (json.RootElement.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String)
            {
                return (modelPath, version.GetString());
            }
        }
        catch
        {
            return (modelPath, null);
        }

        return (modelPath, null);
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

    private static string ResolveLearningSampleDirectory(RpaAutomationOptions options)
    {
        var rawPath = string.IsNullOrWhiteSpace(options.LearningSampleDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AIChat",
                "RpaClient",
                "learning-samples")
            : Environment.ExpandEnvironmentVariables(options.LearningSampleDirectory);

        return Path.IsPathRooted(rawPath)
            ? rawPath
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, rawPath));
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

    private static void CleanupExpiredSamples(string rootDirectory, int retentionDays)
    {
        if (retentionDays <= 0 || !Directory.Exists(rootDirectory))
        {
            return;
        }

        var cutoff = DateTime.Now.AddDays(-retentionDays);
        foreach (var bucket in new[] { "accepted", "review" })
        {
            var directory = Path.Combine(rootDirectory, bucket);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTime < cutoff)
                    {
                        info.Delete();
                    }
                }
                catch
                {
                    // Retention cleanup must not affect the RPA task.
                }
            }
        }
    }

    private sealed record DraftBox(string Label, DrawingRectangle LocalRectangle, decimal Confidence, string Source);
}

public sealed record LearningSampleCaptureResult(
    string Bucket,
    string ImagePath,
    string MetadataPath,
    string LabelPath,
    int DraftLabelCount);
