using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace AIChat.RpaClient.Automation;

public sealed class ScreenCaptureService
{
    public CapturedImage Capture(Rectangle screenBounds)
    {
        if (screenBounds.Width <= 0 || screenBounds.Height <= 0)
        {
            throw new InvalidOperationException("截图区域无效。");
        }

        var bitmap = new Bitmap(screenBounds.Width, screenBounds.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(screenBounds.Location, Point.Empty, screenBounds.Size);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return new CapturedImage(bitmap, stream.ToArray(), screenBounds);
    }
}

public sealed class CapturedImage(Bitmap bitmap, byte[] pngBytes, Rectangle bounds) : IDisposable
{
    public Bitmap Bitmap { get; } = bitmap;
    public byte[] PngBytes { get; } = pngBytes;
    public Rectangle Bounds { get; } = bounds;

    public void Dispose()
    {
        Bitmap.Dispose();
    }
}
