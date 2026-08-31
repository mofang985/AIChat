using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace AIChat.RpaClient.Automation;


public sealed class WeChatWindowLocator
{
    public WeChatWindow? FindByTitleKeyword(string titleKeyword)
    {
        var matches = new List<WeChatWindow>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
            {
                return true;
            }

            if (!TryCreateWindow(handle, out var window) ||
                !MatchesTitleKeyword(handle, window.Title, titleKeyword))
            {
                return true;
            }

            matches.Add(window);
            return true;
        }, IntPtr.Zero);

        var keyword = titleKeyword.Trim();
        return matches
            .Select(window => new ScoredWindow(window, GetWindowMatchScore(window, keyword)))
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .ThenByDescending(match => match.Window.ClientBounds.Width * match.Window.ClientBounds.Height)
            .Select(match => match.Window)
            .FirstOrDefault();
    }

    public WeChatWindow? FindByHandle(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !IsWindow(handle) || !IsWindowVisible(handle) || IsIconic(handle))
        {
            return null;
        }

        return TryCreateWindow(handle, out var window) ? window : null;
    }

    public void Activate(WeChatWindow window)
    {
        if (IsIconic(window.Handle))
        {
            ShowWindow(window.Handle, ShowWindowMaximize);
        }

        SetForegroundWindow(window.Handle);
    }

    private static bool TryCreateWindow(IntPtr handle, out WeChatWindow window)
    {
        window = null!;
        var title = GetWindowTitle(handle);
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        if (!TryGetClientBounds(handle, out var bounds) || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        window = new WeChatWindow(handle, title, bounds, GetMonitorBounds(handle), GetWindowDpi(handle));
        return true;
    }

    private static Rectangle GetMonitorBounds(IntPtr handle)
    {
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return Rectangle.Empty;
        }

        var info = new NativeMonitorInfo
        {
            Size = Marshal.SizeOf<NativeMonitorInfo>()
        };
        return GetMonitorInfo(monitor, ref info)
            ? Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom)
            : Rectangle.Empty;
    }

    private static uint GetWindowDpi(IntPtr handle)
    {
        try
        {
            return GetDpiForWindow(handle);
        }
        catch (EntryPointNotFoundException)
        {
            return 0;
        }
    }

    private static string GetWindowTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static bool TryGetClientBounds(IntPtr handle, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (!GetClientRect(handle, out var rect))
        {
            return false;
        }

        var topLeft = new NativePoint(rect.Left, rect.Top);
        var bottomRight = new NativePoint(rect.Right, rect.Bottom);
        if (!ClientToScreen(handle, ref topLeft) || !ClientToScreen(handle, ref bottomRight))
        {
            return false;
        }

        bounds = Rectangle.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
        return true;
    }

    private static bool MatchesTitleKeyword(IntPtr handle, string title, string keyword)
    {
        return title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            WeChatWindowTitleMatcher.GetProcessFallbackScore(GetProcessName(handle), keyword) > 0;
    }

    private static int GetWindowMatchScore(WeChatWindow window, string keyword)
    {
        var titleScore = WeChatWindowTitleMatcher.GetMatchScore(window.Title, keyword);
        if (titleScore > 0)
        {
            return titleScore;
        }

        return WeChatWindowTitleMatcher.GetProcessFallbackScore(GetProcessName(window.Handle), keyword);
    }

    private static string? GetProcessName(IntPtr handle)
    {
        _ = GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0)
        {
            return null;
        }

        try
        {
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private sealed record ScoredWindow(WeChatWindow Window, int Score);

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    private const int ShowWindowMaximize = 3;
    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder title, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr handle, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }
}
