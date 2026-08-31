using System.Drawing;
using AIChat.RpaClient.Automation;

namespace AIChat.UnitTests;

public sealed class ChatMessageVisualCacheTests
{
    [Fact]
    public void TryGetUnchangedFrame_ShouldReuseResult_WhenFingerprintAndRegionMatch()
    {
        var cache = new ChatMessageVisualCache();
        var region = new Rectangle(10, 20, 300, 400);
        var result = CreateResult("你好");

        cache.RememberFrame(region, 300, 400, "same", result);

        var hit = cache.TryGetUnchangedFrame(region, 300, 400, "same", out var cached);

        Assert.True(hit);
        Assert.Same(result, cached);
    }

    [Fact]
    public void TryGetUnchangedFrame_ShouldNotReuseResult_WhenFingerprintChanges()
    {
        var cache = new ChatMessageVisualCache();
        var region = new Rectangle(10, 20, 300, 400);

        cache.RememberFrame(region, 300, 400, "old", CreateResult("你好"));

        var hit = cache.TryGetUnchangedFrame(region, 300, 400, "new", out _);

        Assert.False(hit);
    }

    [Fact]
    public void CreateBubbleKey_ShouldChange_WhenImageOrSizeChanges()
    {
        var first = ChatMessageVisualCacheKey.CreateBubbleKey(
            [1, 2, 3],
            ChatMessageSenderType.Customer,
            120,
            40);
        var same = ChatMessageVisualCacheKey.CreateBubbleKey(
            [1, 2, 3],
            ChatMessageSenderType.Customer,
            120,
            40);
        var changedSize = ChatMessageVisualCacheKey.CreateBubbleKey(
            [1, 2, 3],
            ChatMessageSenderType.Customer,
            121,
            40);
        var changedImage = ChatMessageVisualCacheKey.CreateBubbleKey(
            [1, 2, 4],
            ChatMessageSenderType.Customer,
            120,
            40);

        Assert.Equal(first, same);
        Assert.NotEqual(first, changedSize);
        Assert.NotEqual(first, changedImage);
    }

    [Fact]
    public void RememberBubble_ShouldEvictOldEntries_WhenMaxEntriesExceeded()
    {
        var cache = new ChatMessageVisualCache();
        var first = ChatMessageVisualCacheKey.CreateBubbleKey([1], ChatMessageSenderType.Customer, 10, 10);
        var second = ChatMessageVisualCacheKey.CreateBubbleKey([2], ChatMessageSenderType.Customer, 10, 10);

        cache.RememberBubble(first, CreateBubbleEntry("第一条"), maxEntries: 1);
        cache.RememberBubble(second, CreateBubbleEntry("第二条"), maxEntries: 1);

        Assert.False(cache.TryGetBubble(first, out _));
        Assert.True(cache.TryGetBubble(second, out var entry));
        Assert.Equal("第二条", entry.MergeResult.OcrResult.Text);
    }

    [Theory]
    [InlineData(false, "Always", false, false)]
    [InlineData(true, "Always", false, true)]
    [InlineData(true, "OnError", false, false)]
    [InlineData(true, "OnError", true, true)]
    [InlineData(true, "Off", true, false)]
    public void ShouldSave_ShouldRespectDebugCaptureMode(
        bool enabled,
        string mode,
        bool isError,
        bool expected)
    {
        Assert.Equal(expected, ChatMessageDebugCapturePolicy.ShouldSave(enabled, mode, isError));
    }

    private static ChatMessageVisualExtractionResult CreateResult(string text)
    {
        return ChatMessageFlowAnalyzer.CreateResult(
        [
            new ChatMessageItem(
                ChatMessageSenderType.Customer,
                text,
                new Rectangle(10, 20, 100, 40),
                0.9m,
                0,
                "Test")
        ],
        "Test");
    }

    private static ChatMessageVisualBubbleCacheEntry CreateBubbleEntry(string text)
    {
        var ocr = new OcrResult(text, 0.9m, "TestOcr");
        return new ChatMessageVisualBubbleCacheEntry(
            ocr,
            new VisionOcrMergeResult(true, ocr, ChatMessageSenderType.Customer, false, null));
    }
}
