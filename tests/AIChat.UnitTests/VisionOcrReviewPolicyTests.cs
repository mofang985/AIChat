using AIChat.RpaClient.Automation;

namespace AIChat.UnitTests;

public sealed class VisionOcrReviewPolicyTests
{
    [Fact]
    public void ShouldReview_ShouldReviewLowConfidenceCustomerOcr()
    {
        var shouldReview = VisionOcrReviewPolicy.ShouldReview(
            new OcrResult("你好", 0.42m, "Paddle"),
            ChatMessageSenderType.Customer,
            enabled: true,
            reviewMode: "SuspiciousOnly",
            minOcrConfidence: 0.65m);

        Assert.True(shouldReview);
    }

    [Fact]
    public void ShouldReview_ShouldReviewSuspiciousShortText()
    {
        var shouldReview = VisionOcrReviewPolicy.ShouldReview(
            new OcrResult("1子", 0.90m, "WindowsOcr"),
            ChatMessageSenderType.Customer,
            enabled: true,
            reviewMode: "SuspiciousOnly",
            minOcrConfidence: 0.65m);

        Assert.True(shouldReview);
    }

    [Fact]
    public void ShouldReview_ShouldNotReviewNormalHighConfidenceText()
    {
        var shouldReview = VisionOcrReviewPolicy.ShouldReview(
            new OcrResult("你好，请问价格是多少？", 0.92m, "WindowsOcr"),
            ChatMessageSenderType.Customer,
            enabled: true,
            reviewMode: "SuspiciousOnly",
            minOcrConfidence: 0.65m);

        Assert.False(shouldReview);
    }

    [Theory]
    [InlineData("我需要/ 你帮我查询一下实时天气")]
    [InlineData("我需要你帮我查询—下实时天气")]
    public void ShouldReview_ShouldReviewChineseTextWithSuspiciousSymbols(string text)
    {
        var shouldReview = VisionOcrReviewPolicy.ShouldReview(
            new OcrResult(text, 0.90m, "WindowsOcr"),
            ChatMessageSenderType.Customer,
            enabled: true,
            reviewMode: "SuspiciousOnly",
            minOcrConfidence: 0.65m);

        Assert.True(shouldReview);
    }

    [Fact]
    public void Merge_ShouldUseHighConfidenceVisionText()
    {
        var merged = VisionOcrMergePolicy.Merge(
            new OcrResult("1子", 0.55m, "Paddle"),
            ChatMessageSenderType.Customer,
            new VisionOcrReviewResult(true, "你好", ChatMessageSenderType.Customer, 0.88m, "Ollama:qwen2.5vl:7b", null, null),
            minVisionConfidence: 0.70m,
            skipWhenVisionFails: true);

        Assert.True(merged.IsUsable);
        Assert.True(merged.UsedVisionReview);
        Assert.Equal("你好", merged.OcrResult.Text);
        Assert.Equal(ChatMessageSenderType.Customer, merged.SenderType);
    }

    [Fact]
    public void Merge_ShouldSkipWhenVisionFailsAndFallbackDisabled()
    {
        var merged = VisionOcrMergePolicy.Merge(
            new OcrResult("1子", 0.55m, "Paddle"),
            ChatMessageSenderType.Customer,
            VisionOcrReviewResult.Failed("Ollama:qwen2.5vl:7b", "timeout"),
            minVisionConfidence: 0.70m,
            skipWhenVisionFails: true);

        Assert.False(merged.IsUsable);
    }

    [Fact]
    public void Merge_ShouldUseVisionSenderType()
    {
        var merged = VisionOcrMergePolicy.Merge(
            new OcrResult("好的", 0.61m, "Paddle"),
            ChatMessageSenderType.Unknown,
            new VisionOcrReviewResult(true, "好的", ChatMessageSenderType.Self, 0.91m, "Ollama:qwen2.5vl:7b", null, null),
            minVisionConfidence: 0.70m,
            skipWhenVisionFails: true);

        Assert.True(merged.IsUsable);
        Assert.Equal(ChatMessageSenderType.Self, merged.SenderType);
    }
}
