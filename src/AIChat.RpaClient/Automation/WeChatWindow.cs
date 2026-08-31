using System.Drawing;

namespace AIChat.RpaClient.Automation;

public sealed record WeChatWindow(
    IntPtr Handle,
    string Title,
    Rectangle ClientBounds,
    Rectangle MonitorBounds,
    uint Dpi);
