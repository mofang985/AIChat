using AIChat.RpaClient;

namespace AIChat.UnitTests;

public sealed class RuntimeLogScreenshotPathExtractorTests
{
    [Theory]
    [InlineData("未读队列调试截图：C:\\Users\\Simon\\AppData\\Local\\AIChat\\RpaClient\\unread-queue-captures\\20260812-151001880-e344a1c421544377824703285f439637-UnreadQueue.png", "C:\\Users\\Simon\\AppData\\Local\\AIChat\\RpaClient\\unread-queue-captures\\20260812-151001880-e344a1c421544377824703285f439637-UnreadQueue.png")]
    [InlineData("OCR 完成。 调试截图：%LOCALAPPDATA%\\AIChat\\RpaClient\\debug-captures\\IncomingMessageOcr.png。", "%LOCALAPPDATA%\\AIChat\\RpaClient\\debug-captures\\IncomingMessageOcr.png")]
    [InlineData("15:10:01 布局截图：\"D:\\captures\\layout.webp\"", "D:\\captures\\layout.webp")]
    public void TryExtract_ShouldReturnImagePathFromScreenshotLog(string message, string expectedPath)
    {
        var extracted = RuntimeLogScreenshotPathExtractor.TryExtract(message, out var screenshotPath);

        Assert.True(extracted);
        Assert.Equal(expectedPath, screenshotPath);
    }

    [Theory]
    [InlineData("客户端注册成功。")]
    [InlineData("未读队列只读扫描完成：识别到 1 个候选。耗时 810 ms。")]
    [InlineData("调试截图：")]
    public void TryExtract_ShouldIgnoreNonScreenshotPathLog(string message)
    {
        var extracted = RuntimeLogScreenshotPathExtractor.TryExtract(message, out var screenshotPath);

        Assert.False(extracted);
        Assert.Equal(string.Empty, screenshotPath);
    }
}
