using System.Drawing;

namespace AIChat.RpaClient.Automation;

public sealed record WeChatWindowLock(
    IntPtr Handle,
    string Title,
    Rectangle ClientBounds,
    Rectangle MonitorBounds,
    uint Dpi,
    DateTimeOffset LockedAtUtc)
{
    public static WeChatWindowLock Capture(WeChatWindow window, DateTimeOffset lockedAtUtc)
    {
        return new WeChatWindowLock(
            window.Handle,
            window.Title,
            window.ClientBounds,
            window.MonitorBounds,
            window.Dpi,
            lockedAtUtc);
    }

    public WeChatWindowLockValidation Validate(WeChatWindow? currentWindow, int boundsTolerancePixels)
    {
        if (currentWindow is null)
        {
            return WeChatWindowLockValidation.Invalid("锁定微信窗口已不存在、不可见或已最小化。", null);
        }

        if (currentWindow.Handle != Handle)
        {
            return WeChatWindowLockValidation.Invalid("锁定微信窗口句柄已变化。", currentWindow);
        }

        if (!string.Equals(currentWindow.Title, Title, StringComparison.Ordinal))
        {
            return WeChatWindowLockValidation.Invalid($"锁定微信窗口标题已变化：{Title} -> {currentWindow.Title}。", currentWindow);
        }

        if (currentWindow.MonitorBounds != MonitorBounds)
        {
            return WeChatWindowLockValidation.Invalid($"锁定微信窗口所在显示器已变化：{FormatRectangle(MonitorBounds)} -> {FormatRectangle(currentWindow.MonitorBounds)}。", currentWindow);
        }

        if (Dpi > 0 && currentWindow.Dpi > 0 && currentWindow.Dpi != Dpi)
        {
            return WeChatWindowLockValidation.Invalid($"锁定微信窗口 DPI 已变化：{Dpi} -> {currentWindow.Dpi}。", currentWindow);
        }

        var tolerance = Math.Max(0, boundsTolerancePixels);
        if (!IsWithinTolerance(ClientBounds, currentWindow.ClientBounds, tolerance))
        {
            return WeChatWindowLockValidation.Invalid($"锁定微信窗口客户区已变化：{FormatRectangle(ClientBounds)} -> {FormatRectangle(currentWindow.ClientBounds)}。", currentWindow);
        }

        return WeChatWindowLockValidation.Valid(currentWindow);
    }

    public string ToDisplayText()
    {
        return $"{Title} / 0x{Handle.ToInt64():X} / Monitor={FormatRectangle(MonitorBounds)} / Client={FormatRectangle(ClientBounds)} / DPI={FormatDpi(Dpi)}";
    }

    private static bool IsWithinTolerance(Rectangle expected, Rectangle actual, int tolerance)
    {
        return Math.Abs(expected.Left - actual.Left) <= tolerance &&
            Math.Abs(expected.Top - actual.Top) <= tolerance &&
            Math.Abs(expected.Width - actual.Width) <= tolerance &&
            Math.Abs(expected.Height - actual.Height) <= tolerance;
    }

    private static string FormatRectangle(Rectangle rectangle)
    {
        return $"X={rectangle.X},Y={rectangle.Y},W={rectangle.Width},H={rectangle.Height}";
    }

    private static string FormatDpi(uint dpi)
    {
        return dpi == 0 ? "未知" : dpi.ToString();
    }
}

public sealed record WeChatWindowLockValidation(bool IsValid, string Reason, WeChatWindow? CurrentWindow)
{
    public static WeChatWindowLockValidation Valid(WeChatWindow currentWindow)
    {
        return new WeChatWindowLockValidation(true, "锁定微信窗口校验通过。", currentWindow);
    }

    public static WeChatWindowLockValidation Invalid(string reason, WeChatWindow? currentWindow)
    {
        return new WeChatWindowLockValidation(false, reason, currentWindow);
    }
}
