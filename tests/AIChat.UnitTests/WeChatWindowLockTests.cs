using System.Drawing;
using AIChat.RpaClient.Automation;

namespace AIChat.UnitTests;

public sealed class WeChatWindowLockTests
{
    [Fact]
    public void Validate_ShouldPassWhenWindowMatchesWithinClientBoundsTolerance()
    {
        var lockedWindow = Window(new IntPtr(123), "微信", new Rectangle(100, 50, 1200, 900));
        var currentWindow = Window(new IntPtr(123), "微信", new Rectangle(104, 54, 1196, 904));
        var target = WeChatWindowLock.Capture(lockedWindow, DateTimeOffset.UtcNow);

        var validation = target.Validate(currentWindow, 8);

        Assert.True(validation.IsValid);
        Assert.Same(currentWindow, validation.CurrentWindow);
    }

    [Fact]
    public void Validate_ShouldFailWhenLockedWindowMovesToAnotherMonitor()
    {
        var target = WeChatWindowLock.Capture(
            Window(new IntPtr(123), "微信", new Rectangle(100, 50, 1200, 900), new Rectangle(0, 0, 1920, 1080)),
            DateTimeOffset.UtcNow);
        var currentWindow = Window(
            new IntPtr(123),
            "微信",
            new Rectangle(2020, 50, 1200, 900),
            new Rectangle(1920, 0, 1920, 1080));

        var validation = target.Validate(currentWindow, 8);

        Assert.False(validation.IsValid);
        Assert.Contains("显示器", validation.Reason);
    }

    [Fact]
    public void Validate_ShouldFailWhenClientBoundsChangeBeyondTolerance()
    {
        var target = WeChatWindowLock.Capture(
            Window(new IntPtr(123), "微信", new Rectangle(100, 50, 1200, 900)),
            DateTimeOffset.UtcNow);
        var currentWindow = Window(new IntPtr(123), "微信", new Rectangle(130, 50, 1200, 900));

        var validation = target.Validate(currentWindow, 8);

        Assert.False(validation.IsValid);
        Assert.Contains("客户区", validation.Reason);
    }

    [Fact]
    public void Validate_ShouldFailWhenWindowHandleNoLongerExists()
    {
        var target = WeChatWindowLock.Capture(
            Window(new IntPtr(123), "微信", new Rectangle(100, 50, 1200, 900)),
            DateTimeOffset.UtcNow);

        var validation = target.Validate(null, 8);

        Assert.False(validation.IsValid);
        Assert.Contains("不存在", validation.Reason);
    }

    private static WeChatWindow Window(IntPtr handle, string title, Rectangle clientBounds)
    {
        return Window(handle, title, clientBounds, new Rectangle(0, 0, 1920, 1080));
    }

    private static WeChatWindow Window(IntPtr handle, string title, Rectangle clientBounds, Rectangle monitorBounds)
    {
        return new WeChatWindow(handle, title, clientBounds, monitorBounds, 144);
    }
}
