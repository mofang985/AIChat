using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace AIChat.RpaClient.Automation;

public sealed class PaddleOcrEngine : IDisposable
{
    private readonly Lazy<PaddleOcrAll> _ocr = new(CreateOcr);
    private readonly Lazy<OcrEngine?> _windowsOcr = new(OcrEngine.TryCreateFromUserProfileLanguages);

    public async Task<OcrResult> RecognizeAsync(byte[] imageBytes, CancellationToken cancellationToken)
    {
        var windowsResult = await TryRecognizeWithWindowsOcrAsync(imageBytes, cancellationToken, preserveUiText: false);
        if (OcrResultSelector.GetQualityScore(windowsResult) >= 0.65d)
        {
            return windowsResult;
        }

        var paddleResult = await RecognizeWithPaddleAsync(imageBytes, cancellationToken);
        return OcrResultSelector.ChooseBetter(windowsResult, paddleResult);
    }

    public async Task<OcrResult> RecognizeAccurateAsync(byte[] imageBytes, CancellationToken cancellationToken)
    {
        var windowsResult = await TryRecognizeWithWindowsOcrAsync(imageBytes, cancellationToken, preserveUiText: false);
        var paddleResult = await RecognizeWithPaddleAsync(imageBytes, cancellationToken);
        return OcrResultSelector.ChooseBetter(windowsResult, paddleResult);
    }

    public Task<OcrResult> RecognizeUiTextAsync(byte[] imageBytes, CancellationToken cancellationToken)
    {
        return TryRecognizeWithWindowsOcrAsync(imageBytes, cancellationToken, preserveUiText: true);
    }

    private Task<OcrResult> RecognizeWithPaddleAsync(byte[] imageBytes, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var image = Cv2.ImDecode(imageBytes, ImreadModes.Color);
            if (image.Empty())
            {
                return new OcrResult(string.Empty, 0, "EmptyImage");
            }

            var originalResult = RunOcr(image, "Original");
            using var focusedImage = TryCreateFocusedImage(image);
            if (focusedImage is null || focusedImage.Empty())
            {
                return originalResult;
            }

            var focusedResult = RunOcr(focusedImage, "FocusedCrop");
            var bestResult = OcrResultSelector.ChooseBetter(originalResult, focusedResult);

            using var enhancedImage = TryCreateTextEnhancedImage(image);
            if (enhancedImage is null || enhancedImage.Empty())
            {
                return bestResult;
            }

            var enhancedResult = RunOcr(enhancedImage, "TextEnhancedCrop");
            return OcrResultSelector.ChooseBetter(bestResult, enhancedResult);
        }, cancellationToken);
    }

    private async Task<OcrResult> TryRecognizeWithWindowsOcrAsync(byte[] imageBytes, CancellationToken cancellationToken, bool preserveUiText)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(imageBytes.AsBuffer());
            stream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var engine = _windowsOcr.Value;
            if (engine is null)
            {
                return new OcrResult(string.Empty, 0, "WindowsOcrUnavailable");
            }

            var result = await engine.RecognizeAsync(bitmap);
            cancellationToken.ThrowIfCancellationRequested();

            var lines = result.Lines
                .Select(line => preserveUiText ? CleanUiOcrLine(line.Text) : CleanOcrLine(line.Text))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();

            var text = string.Join(Environment.NewLine, lines);
            var confidence = string.IsNullOrWhiteSpace(text) ? 0m : 0.90m;
            return new OcrResult(text.Trim(), confidence, preserveUiText ? "WindowsOcrUi" : "WindowsOcr");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new OcrResult(string.Empty, 0, $"WindowsOcrFailed:{ex.GetType().Name}");
        }
    }

    public void Dispose()
    {
        if (_ocr.IsValueCreated)
        {
            _ocr.Value.Dispose();
        }
    }

    private static PaddleOcrAll CreateOcr()
    {
        var model = LocalFullModels.ChineseV5;
        return new PaddleOcrAll(model, PaddleDevice.Mkldnn())
        {
            AllowRotateDetection = true,
            Enable180Classification = false
        };
    }

    private OcrResult RunOcr(Mat image, string source)
    {
        var result = _ocr.Value.Run(image);
        var lines = result.Regions
            .Select(region => new OcrLine(CleanOcrLine(region.Text), (double)region.Score))
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
            .ToArray();

        var text = string.Join(Environment.NewLine, lines.Select(x => x.Text));
        var confidence = CalculateConfidence(lines.Select(x => x.Score));
        return new OcrResult(text.Trim(), confidence, source);
    }

    private static Mat? TryCreateFocusedImage(Mat image)
    {
        using var gray = new Mat();
        Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);

        using var mask = new Mat();
        Cv2.Threshold(gray, mask, 248, 255, ThresholdTypes.BinaryInv);

        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(7, 5));
        using var dilated = new Mat();
        Cv2.Dilate(mask, dilated, kernel, iterations: 2);

        var contentBounds = FindContentBounds(dilated, image.Width, image.Height);
        if (contentBounds is null)
        {
            return null;
        }

        var paddedBounds = AddPadding(contentBounds.Value, image.Width, image.Height, 28, 20);
        if (paddedBounds.Width <= 0 || paddedBounds.Height <= 0)
        {
            return null;
        }

        using var cropped = new Mat(image, paddedBounds);
        var scale = CalculateScaleFactor(cropped.Width, cropped.Height);
        var focused = new Mat();
        Cv2.Resize(cropped, focused, new Size(), scale, scale, InterpolationFlags.Lanczos4);
        return focused;
    }

    private static Mat? TryCreateTextEnhancedImage(Mat image)
    {
        using var gray = new Mat();
        Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);

        using var textMask = new Mat();
        Cv2.Threshold(gray, textMask, 185, 255, ThresholdTypes.BinaryInv);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 2));
        Cv2.MorphologyEx(textMask, textMask, MorphTypes.Close, kernel);

        var contentBounds = FindContentBounds(textMask, image.Width, image.Height);
        if (contentBounds is null)
        {
            return null;
        }

        var paddedBounds = AddPadding(contentBounds.Value, image.Width, image.Height, 18, 14);
        if (paddedBounds.Width <= 0 || paddedBounds.Height <= 0)
        {
            return null;
        }

        using var croppedGray = new Mat(gray, paddedBounds);
        using var binary = new Mat();
        Cv2.Threshold(croppedGray, binary, 205, 255, ThresholdTypes.Binary);

        var scale = CalculateScaleFactor(binary.Width, binary.Height);
        using var scaled = new Mat();
        Cv2.Resize(binary, scaled, new Size(), scale, scale, InterpolationFlags.Nearest);
        using var bordered = new Mat();
        Cv2.CopyMakeBorder(
            scaled,
            bordered,
            24,
            24,
            30,
            30,
            BorderTypes.Constant,
            Scalar.White);
        var enhanced = new Mat();
        Cv2.CvtColor(bordered, enhanced, ColorConversionCodes.GRAY2BGR);
        return enhanced;
    }

    private static Rect? FindContentBounds(Mat mask, int imageWidth, int imageHeight)
    {
        Rect? bounds = null;
        var contours = Cv2.FindContoursAsArray(mask, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        foreach (var contour in contours)
        {
            var rect = Cv2.BoundingRect(contour);
            if (!IsUsefulContentRect(rect, imageWidth, imageHeight))
            {
                continue;
            }

            bounds = bounds is null ? rect : Union(bounds.Value, rect);
        }

        if (bounds is null)
        {
            return null;
        }

        var current = bounds.Value;
        var coversMostImage = current.Width > imageWidth * 0.9 && current.Height > imageHeight * 0.9;
        return coversMostImage ? null : current;
    }

    private static bool IsUsefulContentRect(Rect rect, int imageWidth, int imageHeight)
    {
        if (rect.Width < 3 || rect.Height < 3 || rect.Width * rect.Height < 25)
        {
            return false;
        }

        var looksLikeHorizontalDivider = rect.Width > imageWidth * 0.85 && rect.Height < 18;
        var looksLikeVerticalDivider = rect.Height > imageHeight * 0.85 && rect.Width < 18;
        return !looksLikeHorizontalDivider && !looksLikeVerticalDivider;
    }

    private static Rect Union(Rect first, Rect second)
    {
        var left = Math.Min(first.Left, second.Left);
        var top = Math.Min(first.Top, second.Top);
        var right = Math.Max(first.Right, second.Right);
        var bottom = Math.Max(first.Bottom, second.Bottom);
        return Rect.FromLTRB(left, top, right, bottom);
    }

    private static Rect AddPadding(Rect rect, int imageWidth, int imageHeight, int paddingX, int paddingY)
    {
        var left = Math.Max(0, rect.Left - paddingX);
        var top = Math.Max(0, rect.Top - paddingY);
        var right = Math.Min(imageWidth, rect.Right + paddingX);
        var bottom = Math.Min(imageHeight, rect.Bottom + paddingY);
        return Rect.FromLTRB(left, top, right, bottom);
    }

    private static double CalculateScaleFactor(int width, int height)
    {
        var scale = 1.0d;
        if (width < 900)
        {
            scale = Math.Max(scale, 900d / Math.Max(1, width));
        }

        if (height < 180)
        {
            scale = Math.Max(scale, 180d / Math.Max(1, height));
        }

        return Math.Clamp(scale, 1.25d, 3.5d);
    }

    private static string CleanUiOcrLine(string? text)
    {
        return NormalizeOcrSpaces(text?.Trim() ?? string.Empty);
    }

    private static string CleanOcrLine(string? text)
    {
        var trimmed = NormalizeOcrSpaces(text?.Trim() ?? string.Empty);
        if (trimmed.Length == 0 || LooksLikeTimeText(trimmed))
        {
            return string.Empty;
        }

        if (trimmed.Length == 1 && trimmed[0] < 128)
        {
            return string.Empty;
        }

        return CustomerMessageTextCorrector.Correct(trimmed);
    }

    private static bool LooksLikeTimeText(string text)
    {
        var compact = new string(text.Where(value => !char.IsWhiteSpace(value)).ToArray());
        var parts = compact.Replace('：', ':').Split(':');
        return parts.Length == 2 &&
            parts[0].Length is >= 1 and <= 2 &&
            parts[1].Length == 2 &&
            parts[0].All(char.IsDigit) &&
            parts[1].All(char.IsDigit);
    }

    private static string NormalizeOcrSpaces(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (!char.IsWhiteSpace(current))
            {
                builder.Append(current);
                continue;
            }

            var previous = FindPreviousNonWhiteSpace(value, i - 1);
            var next = FindNextNonWhiteSpace(value, i + 1);
            if (previous.HasValue && next.HasValue && IsCjk(previous.Value) && IsCjk(next.Value))
            {
                continue;
            }

            if (builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }
        }

        return builder.ToString().Trim();
    }

    private static char? FindPreviousNonWhiteSpace(string value, int startIndex)
    {
        for (var i = startIndex; i >= 0; i--)
        {
            if (!char.IsWhiteSpace(value[i]))
            {
                return value[i];
            }
        }

        return null;
    }

    private static char? FindNextNonWhiteSpace(string value, int startIndex)
    {
        for (var i = startIndex; i < value.Length; i++)
        {
            if (!char.IsWhiteSpace(value[i]))
            {
                return value[i];
            }
        }

        return null;
    }

    private static bool IsCjk(char value)
    {
        return value is >= '\u4e00' and <= '\u9fff';
    }

    private static decimal CalculateConfidence(IEnumerable<double> scores)
    {
        var normalizedScores = scores
            .Where(double.IsFinite)
            .Select(score => Math.Clamp(score, 0d, 1d))
            .ToArray();

        return normalizedScores.Length == 0 ? 0m : (decimal)normalizedScores.Average();
    }
}

public sealed record OcrLine(string Text, double Score);
