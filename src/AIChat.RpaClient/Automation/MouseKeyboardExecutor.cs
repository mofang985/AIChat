using System.Drawing;
using System.Runtime.InteropServices;
using AIChat.RpaClient.Configuration;

namespace AIChat.RpaClient.Automation;

public sealed class MouseKeyboardExecutor
{
    public async Task ClickAsync(Point screenPoint, int waitMs, CancellationToken cancellationToken)
    {
        await ClickAsync(screenPoint, waitMs, null, cancellationToken);
    }

    public async Task ClickAsync(Point screenPoint, int waitMs, RpaAutomationOptions? options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (options?.HumanizeInput != true)
        {
            SetCursorPos(screenPoint.X, screenPoint.Y);
            await Task.Delay(Math.Max(0, waitMs), cancellationToken);
            mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
            await Task.Delay(60, cancellationToken);
            mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
            await Task.Delay(Math.Max(0, waitMs), cancellationToken);
            return;
        }

        var targetPoint = ApplyTargetJitter(screenPoint, options.ClickJitterPixels);
        await MoveCursorAsync(targetPoint, options, cancellationToken);
        await DelayWithJitterAsync(waitMs, 0.25d, cancellationToken);
        mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
        await Task.Delay(NextInRange(options.ClickDownMsMin, options.ClickDownMsMax), cancellationToken);
        mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
        await DelayWithJitterAsync(waitMs, 0.25d, cancellationToken);
    }

    public async Task TypeTextAsync(string text, int keyWaitMs, CancellationToken cancellationToken)
    {
        await TypeTextAsync(text, keyWaitMs, null, cancellationToken);
    }

    public async Task TypeTextAsync(string text, int keyWaitMs, RpaAutomationOptions? options, CancellationToken cancellationToken)
    {
        if (options?.InputMode.Equals("ClipboardPaste", StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                await PasteTextAsync(text, Math.Max(keyWaitMs, options.ClickWaitMs), cancellationToken);
                return;
            }
            catch (InvalidOperationException) when (options.EnableKeyboardFallbackOnClipboardFailure)
            {
                await TypeTextByKeyboardAsync(text, keyWaitMs, options, cancellationToken);
                return;
            }
        }

        await TypeTextByKeyboardAsync(text, keyWaitMs, options, cancellationToken);
    }

    private static async Task TypeTextByKeyboardAsync(string text, int keyWaitMs, RpaAutomationOptions? options, CancellationToken cancellationToken)
    {
        foreach (var character in text)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (options?.HumanizeInput == true)
            {
                SendUnicodeKey(character, keyUp: false);
                await Task.Delay(NextInRange(options.KeyPressMsMin, options.KeyPressMsMax), cancellationToken);
                SendUnicodeKey(character, keyUp: true);
                await Task.Delay(NextInRange(options.KeyDelayMsMin, options.KeyDelayMsMax), cancellationToken);

                if (ShouldInsertTypingPause(character, options.TypingPauseChance))
                {
                    await Task.Delay(NextInRange(options.TypingPauseMsMin, options.TypingPauseMsMax), cancellationToken);
                }
            }
            else
            {
                SendUnicodeCharacter(character);
                await Task.Delay(Math.Max(0, keyWaitMs), cancellationToken);
            }
        }
    }

    public async Task PasteTextAsync(string text, int waitMs, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetClipboardTextWithRetry(text);
        await Task.Delay(Math.Max(80, waitMs), cancellationToken);
        SendCtrlVShortcut();
        await Task.Delay(Math.Max(120, waitMs), cancellationToken);
    }

    public async Task PressEnterAsync(int waitMs, CancellationToken cancellationToken)
    {
        await PressEnterAsync(waitMs, null, cancellationToken);
    }

    public async Task PressEnterAsync(int waitMs, RpaAutomationOptions? options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SendVirtualKey(VirtualKeyReturn, keyUp: false);
        await Task.Delay(options?.HumanizeInput == true
            ? NextInRange(options.KeyPressMsMin, options.KeyPressMsMax)
            : 60, cancellationToken);
        SendVirtualKey(VirtualKeyReturn, keyUp: true);
        if (options?.HumanizeInput == true)
        {
            await DelayWithJitterAsync(waitMs, 0.25d, cancellationToken);
        }
        else
        {
            await Task.Delay(Math.Max(0, waitMs), cancellationToken);
        }
    }

    public async Task SelectAllAsync(int waitMs, RpaAutomationOptions? options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SendCtrlAShortcut();
        if (options?.HumanizeInput == true)
        {
            await DelayWithJitterAsync(waitMs, 0.25d, cancellationToken);
        }
        else
        {
            await Task.Delay(Math.Max(0, waitMs), cancellationToken);
        }
    }

    public async Task ClearTextAsync(int waitMs, RpaAutomationOptions? options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await SelectAllAsync(waitMs, options, cancellationToken);
        SendBackspaceKey();
        if (options?.HumanizeInput == true)
        {
            await DelayWithJitterAsync(waitMs, 0.25d, cancellationToken);
        }
        else
        {
            await Task.Delay(Math.Max(0, waitMs), cancellationToken);
        }
    }

    private static async Task MoveCursorAsync(Point targetPoint, RpaAutomationOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!GetCursorPos(out var currentPoint))
        {
            SetCursorPos(targetPoint.X, targetPoint.Y);
            return;
        }

        var startX = currentPoint.X;
        var startY = currentPoint.Y;
        var deltaX = targetPoint.X - startX;
        var deltaY = targetPoint.Y - startY;
        var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        var steps = Math.Max(1, NextInRange(options.MouseMoveStepsMin, options.MouseMoveStepsMax));
        var durationMs = Math.Max(0, NextInRange(options.MouseMoveDurationMsMin, options.MouseMoveDurationMsMax));

        if (distance <= 1d || durationMs == 0 || steps == 1)
        {
            SetCursorPos(targetPoint.X, targetPoint.Y);
            return;
        }

        var delayMs = Math.Max(1, durationMs / steps);
        var perpendicularX = -deltaY / distance;
        var perpendicularY = deltaX / distance;
        var pathJitterPixels = Math.Max(0, options.MouseMoveJitterPixels);

        for (var step = 1; step <= steps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var progress = step / (double)steps;
            var easedProgress = SmoothStep(progress);
            var jitterWeight = step == steps ? 0d : Math.Sin(Math.PI * progress);
            var jitter = pathJitterPixels == 0
                ? 0d
                : (Random.Shared.NextDouble() * 2d - 1d) * pathJitterPixels * jitterWeight;

            var x = (int)Math.Round(startX + deltaX * easedProgress + perpendicularX * jitter);
            var y = (int)Math.Round(startY + deltaY * easedProgress + perpendicularY * jitter);
            SetCursorPos(x, y);
            await Task.Delay(delayMs, cancellationToken);
        }

        SetCursorPos(targetPoint.X, targetPoint.Y);
    }

    private static Point ApplyTargetJitter(Point point, int jitterPixels)
    {
        var jitter = Math.Max(0, jitterPixels);
        if (jitter == 0)
        {
            return point;
        }

        return new Point(
            point.X + Random.Shared.Next(-jitter, jitter + 1),
            point.Y + Random.Shared.Next(-jitter, jitter + 1));
    }

    private static async Task DelayWithJitterAsync(int baseDelayMs, double ratio, CancellationToken cancellationToken)
    {
        var delayMs = Math.Max(0, baseDelayMs);
        if (delayMs == 0)
        {
            return;
        }

        var spread = Math.Max(1, (int)Math.Round(delayMs * Math.Max(0d, ratio)));
        var randomizedDelay = Math.Max(0, delayMs + Random.Shared.Next(-spread, spread + 1));
        await Task.Delay(randomizedDelay, cancellationToken);
    }

    private static int NextInRange(int min, int max)
    {
        var low = Math.Max(0, Math.Min(min, max));
        var high = Math.Max(0, Math.Max(min, max));
        return low == high ? low : Random.Shared.Next(low, high + 1);
    }

    private static double SmoothStep(double value)
    {
        return value * value * (3d - 2d * value);
    }

    private static bool ShouldInsertTypingPause(char character, decimal configuredChance)
    {
        var chance = Math.Clamp((double)configuredChance, 0d, 1d);
        if (character is '，' or '。' or '！' or '？' or ',' or '.' or '!' or '?' or ';' or '；' or '\n')
        {
            chance = Math.Min(1d, chance + 0.12d);
        }

        return chance > 0d && Random.Shared.NextDouble() < chance;
    }

    private static void SendUnicodeCharacter(char character)
    {
        var inputs = new[]
        {
            CreateKeyboardInput(character, keyUp: false),
            CreateKeyboardInput(character, keyUp: true)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            var errorCode = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"键盘输入失败，Win32Error={errorCode}。");
        }
    }

    private static void SendVirtualKey(ushort virtualKey, bool keyUp)
    {
        var inputs = new[]
        {
            CreateVirtualKeyInput(virtualKey, keyUp)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            var errorCode = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"按键输入失败，Win32Error={errorCode}。");
        }
    }

    private static void SendCtrlAShortcut()
    {
        SendCtrlShortcut(VirtualKeyA, "全选快捷键");
    }

    private static void SendBackspaceKey()
    {
        var inputs = new[]
        {
            CreateVirtualKeyInput(VirtualKeyBackspace, keyUp: false),
            CreateVirtualKeyInput(VirtualKeyBackspace, keyUp: true)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            var errorCode = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"清空输入框失败，Win32Error={errorCode}。");
        }
    }

    private static void SendCtrlShortcut(ushort virtualKey, string actionName)
    {
        var inputs = new[]
        {
            CreateVirtualKeyInput(VirtualKeyControl, keyUp: false),
            CreateVirtualKeyInput(virtualKey, keyUp: false),
            CreateVirtualKeyInput(virtualKey, keyUp: true),
            CreateVirtualKeyInput(VirtualKeyControl, keyUp: true)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            var errorCode = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"{actionName}输入失败，Win32Error={errorCode}。");
        }
    }

    private static void SendCtrlVShortcut()
    {
        SendCtrlShortcut(VirtualKeyV, "粘贴快捷键");
    }

    private static void SetClipboardTextWithRetry(string text)
    {
        Exception? lastException = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetText(text, System.Windows.TextDataFormat.UnicodeText);
                return;
            }
            catch (Exception ex) when (ex is ExternalException or ThreadStateException)
            {
                lastException = ex;
                Thread.Sleep(80 * attempt);
            }
        }

        throw new InvalidOperationException($"写入剪贴板失败：{lastException?.Message}");
    }

    private static void SendUnicodeKey(char character, bool keyUp)
    {
        var inputs = new[]
        {
            CreateKeyboardInput(character, keyUp)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            var errorCode = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"键盘输入失败，Win32Error={errorCode}。");
        }
    }

    private static Input CreateKeyboardInput(char character, bool keyUp)
    {
        return new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = 0,
                    ScanCode = character,
                    Flags = KeyEventUnicode | (keyUp ? KeyEventKeyUp : 0),
                    Time = 0,
                    ExtraInfo = UIntPtr.Zero
                }
            }
        };
    }

    private static Input CreateVirtualKeyInput(ushort virtualKey, bool keyUp)
    {
        return new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    ScanCode = 0,
                    Flags = keyUp ? KeyEventKeyUp : 0,
                    Time = 0,
                    ExtraInfo = UIntPtr.Zero
                }
            }
        };
    }

    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const int InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;
    private const ushort VirtualKeyA = 0x41;
    private const ushort VirtualKeyBackspace = 0x08;
    private const ushort VirtualKeyControl = 0x11;
    private const ushort VirtualKeyReturn = 0x0D;
    private const ushort VirtualKeyV = 0x56;

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public int Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;

        // SendInput 的 INPUT union 在 64 位下按 MOUSEINPUT 的最大尺寸对齐。
        [FieldOffset(0)]
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
