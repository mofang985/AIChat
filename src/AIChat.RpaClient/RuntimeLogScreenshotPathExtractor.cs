namespace AIChat.RpaClient;

internal static class RuntimeLogScreenshotPathExtractor
{
    private const string ScreenshotMarker = "截图：";
    private static readonly string[] SupportedImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".webp"];

    public static bool TryExtract(string? message, out string screenshotPath)
    {
        screenshotPath = string.Empty;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var markerIndex = message.LastIndexOf(ScreenshotMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        var pathStart = markerIndex + ScreenshotMarker.Length;
        while (pathStart < message.Length && char.IsWhiteSpace(message[pathStart]))
        {
            pathStart++;
        }

        if (pathStart >= message.Length)
        {
            return false;
        }

        var candidate = TrimPathBoundary(message.AsSpan(pathStart));
        var pathLength = FindImagePathLength(candidate);
        if (pathLength <= 0)
        {
            return false;
        }

        screenshotPath = TrimPathBoundary(candidate[..pathLength]).ToString();
        return screenshotPath.Length > 0;
    }

    private static ReadOnlySpan<char> TrimPathBoundary(ReadOnlySpan<char> value)
    {
        return value.Trim().Trim("\"'“”。；;".AsSpan());
    }

    private static int FindImagePathLength(ReadOnlySpan<char> value)
    {
        var pathLength = -1;
        foreach (var extension in SupportedImageExtensions)
        {
            var extensionIndex = value.LastIndexOf(extension, StringComparison.OrdinalIgnoreCase);
            if (extensionIndex < 0)
            {
                continue;
            }

            var extensionEnd = extensionIndex + extension.Length;
            if (extensionEnd > pathLength)
            {
                pathLength = extensionEnd;
            }
        }

        return pathLength;
    }
}
