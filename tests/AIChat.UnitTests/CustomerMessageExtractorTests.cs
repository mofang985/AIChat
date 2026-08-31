using AIChat.RpaClient.Automation;

namespace AIChat.UnitTests;

public sealed class CustomerMessageExtractorTests
{
    [Fact]
    public void ExtractLatest_ReturnsLastUsefulLine()
    {
        var latest = CustomerMessageExtractor.ExtractLatest("""
            你好

            这个多少钱
            """);

        Assert.Equal("这个多少钱", latest);
    }

    [Fact]
    public void ExtractSnapshot_UsesConversationContextAndLineCount()
    {
        var snapshot = CustomerMessageExtractor.ExtractSnapshot(
            """
            第一条
            第二条
            """,
            """
            客户：第一条
            客服：您好
            客户：第二条
            """);

        Assert.NotNull(snapshot);
        Assert.Equal("第二条", snapshot.LatestMessage);
        Assert.Equal(2, snapshot.RawLineCount);
        Assert.Contains("客服：您好", snapshot.ConversationContext);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Fingerprint));
    }

    [Fact]
    public void ExtractSnapshot_AppendsLatestMessageWhenContextMissesIt()
    {
        var snapshot = CustomerMessageExtractor.ExtractSnapshot(
            "你好",
            "客户：我是Hzq");

        Assert.NotNull(snapshot);
        Assert.Equal("你好", snapshot.LatestMessage);
        Assert.Contains("客户：我是Hzq", snapshot.ConversationContext);
        Assert.Contains("你好", snapshot.ConversationContext);
        Assert.False(snapshot.HasConversationTextAfterLatestMessage);
    }

    [Fact]
    public void ExtractSnapshot_MarksLatestMessageAsUnrepliedWhenItIsLastInContext()
    {
        var snapshot = CustomerMessageExtractor.ExtractSnapshot(
            "你好",
            """
            客户：我是Hzq
            客户：你好
            """);

        Assert.NotNull(snapshot);
        Assert.Equal("你好", snapshot.LatestMessage);
        Assert.False(snapshot.HasConversationTextAfterLatestMessage);
    }

    [Fact]
    public void ExtractSnapshot_MarksLatestMessageAsRepliedWhenContextHasFollowingText()
    {
        var snapshot = CustomerMessageExtractor.ExtractSnapshot(
            "你好",
            """
            客户：我是Hzq
            客户：你好
            好的，有什么可以帮您的？
            """);

        Assert.NotNull(snapshot);
        Assert.Equal("你好", snapshot.LatestMessage);
        Assert.True(snapshot.HasConversationTextAfterLatestMessage);
    }

    [Fact]
    public void ExtractSnapshot_DoesNotAppendLatestMessageWhenContextUsesSpeakerPrefix()
    {
        var snapshot = CustomerMessageExtractor.ExtractSnapshot(
            "你好",
            """
            客户：我是Hzq
            客户：你好
            """);

        Assert.NotNull(snapshot);
        Assert.Equal("你好", snapshot.LatestMessage);
        Assert.Equal(2, snapshot.ConversationContext.Split(Environment.NewLine).Length);
    }

    [Fact]
    public void ExtractSnapshot_ChangesFingerprintWhenLatestMessageChanges()
    {
        var first = CustomerMessageExtractor.ExtractSnapshot("测试商品多少钱");
        var second = CustomerMessageExtractor.ExtractSnapshot("测试商品有库存吗");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void ExtractSnapshot_IgnoresWeChatSystemNoticeWhenChoosingLatestMessage()
    {
        var snapshot = CustomerMessageExtractor.ExtractSnapshot("""
            我是Hzq
            你已添加了Hzq，现在可以开始聊天了。
            """);

        Assert.NotNull(snapshot);
        Assert.Equal("我是Hzq", snapshot.LatestMessage);
    }

    [Fact]
    public void ExtractSnapshot_ReturnsNullWhenOnlySystemNoticeIsRecognized()
    {
        var snapshot = CustomerMessageExtractor.ExtractSnapshot(
            """
            你已添加了
            现在可以开始聊天了
            """,
            """
            你已添加了Hzq，现在可以开始聊天了。
            好的，可以的。
            """);

        Assert.Null(snapshot);
    }

    [Fact]
    public void NormalizeForComparison_RemovesWhitespaceAndPunctuationNoise()
    {
        var normalized = CustomerMessageExtractor.NormalizeForComparison(" 测试 商品？ 123 ");

        Assert.Equal("测试商品123", normalized);
    }

    [Fact]
    public void AreSameMessage_TreatsSpeakerPrefixAsSameMessage()
    {
        Assert.True(CustomerMessageExtractor.AreSameMessage("客户：你叫什么", "你叫什么"));
    }

    [Fact]
    public void ContainsComparableText_UsesNormalizedText()
    {
        Assert.True(CustomerMessageExtractor.ContainsComparableText(
            """
            客户：你好
            好的，有什么可以帮您的？
            """,
            "好的，有什么可以帮您的"));
    }

    [Fact]
    public void CreateBottomUpScanRegions_ReturnsBottomRegionFirstAndFullRegionLast()
    {
        var fullRegion = new System.Drawing.Rectangle(100, 200, 500, 1000);

        var regions = CustomerMessageOcrScanPlanner.CreateBottomUpScanRegions(fullRegion);

        Assert.NotEmpty(regions);
        Assert.True(regions[0].Bottom == fullRegion.Bottom);
        Assert.Equal(fullRegion, regions[^1]);
        Assert.True(regions[0].Top > fullRegion.Top);
    }
}
