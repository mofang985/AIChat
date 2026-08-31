using System.Drawing;
using System.Security.Cryptography;
using System.Text;

namespace AIChat.RpaClient.Automation;

public sealed class ChatMessageVisualCache
{
    private readonly Dictionary<string, ChatMessageVisualBubbleCacheEntry> _bubbleEntries = new(StringComparer.Ordinal);
    private readonly Queue<string> _bubbleEntryOrder = new();
    private ChatMessageVisualFrameCacheEntry? _lastFrame;

    public void Clear()
    {
        _lastFrame = null;
        _bubbleEntries.Clear();
        _bubbleEntryOrder.Clear();
    }

    public bool TryGetUnchangedFrame(
        Rectangle region,
        int imageWidth,
        int imageHeight,
        string fingerprint,
        out ChatMessageVisualExtractionResult result)
    {
        if (_lastFrame is not null &&
            _lastFrame.Region == region &&
            _lastFrame.ImageWidth == imageWidth &&
            _lastFrame.ImageHeight == imageHeight &&
            string.Equals(_lastFrame.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            result = _lastFrame.Result;
            return true;
        }

        result = null!;
        return false;
    }

    public void RememberFrame(
        Rectangle region,
        int imageWidth,
        int imageHeight,
        string fingerprint,
        ChatMessageVisualExtractionResult result)
    {
        _lastFrame = new ChatMessageVisualFrameCacheEntry(region, imageWidth, imageHeight, fingerprint, result);
    }

    public bool TryGetBubble(string key, out ChatMessageVisualBubbleCacheEntry entry)
    {
        return _bubbleEntries.TryGetValue(key, out entry!);
    }

    public void RememberBubble(string key, ChatMessageVisualBubbleCacheEntry entry, int maxEntries)
    {
        if (maxEntries <= 0)
        {
            return;
        }

        if (!_bubbleEntries.ContainsKey(key))
        {
            _bubbleEntryOrder.Enqueue(key);
        }

        _bubbleEntries[key] = entry;
        while (_bubbleEntries.Count > maxEntries && _bubbleEntryOrder.Count > 0)
        {
            var expiredKey = _bubbleEntryOrder.Dequeue();
            _bubbleEntries.Remove(expiredKey);
        }
    }

    public void ResetScopeIfChanged(ChatMessageVisualCacheScope scope)
    {
        if (_lastFrame is null)
        {
            return;
        }

        if (_lastFrame.Region != scope.ConversationRegion)
        {
            Clear();
        }
    }
}

public sealed record ChatMessageVisualCacheScope(
    IntPtr WindowHandle,
    Rectangle ClientBounds,
    Rectangle ConversationRegion);

public sealed record ChatMessageVisualBubbleCacheEntry(
    OcrResult BaseOcrResult,
    VisionOcrMergeResult MergeResult);

public sealed record ChatMessageVisualFrameCacheEntry(
    Rectangle Region,
    int ImageWidth,
    int ImageHeight,
    string Fingerprint,
    ChatMessageVisualExtractionResult Result);

public static class ChatMessageVisualCacheKey
{
    public static string CreateBubbleKey(
        byte[] pngBytes,
        ChatMessageSenderType candidateSenderType,
        int width,
        int height)
    {
        var imageHash = Convert.ToHexString(SHA256.HashData(pngBytes)).ToLowerInvariant();
        return $"{candidateSenderType}|{width}x{height}|{imageHash}";
    }

    public static string CreateFrameKey(byte[] imageBytes)
    {
        return Convert.ToHexString(SHA256.HashData(imageBytes)).ToLowerInvariant();
    }

    public static string CreateScopeKey(ChatMessageVisualCacheScope scope)
    {
        var raw = $"{scope.WindowHandle}|{scope.ClientBounds}|{scope.ConversationRegion}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }
}

public static class ChatMessageDebugCapturePolicy
{
    public static bool ShouldSave(bool enabled, string? mode, bool isError)
    {
        if (!enabled)
        {
            return false;
        }

        if (string.Equals(mode, "Off", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(mode, "OnError", StringComparison.OrdinalIgnoreCase))
        {
            return isError;
        }

        return true;
    }
}
