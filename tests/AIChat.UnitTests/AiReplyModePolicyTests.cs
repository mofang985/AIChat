using AIChat.Application.AI;

namespace AIChat.UnitTests;

public sealed class AiReplyModePolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("KnowledgeFirst")]
    [InlineData("knowledgefirst")]
    public void Parse_ShouldUseKnowledgeFirst_WhenValueIsEmptyOrKnowledgeFirst(string? value)
    {
        var result = AiReplyModePolicy.Parse(value);

        Assert.True(result.IsValid);
        Assert.Equal(AiReplyMode.KnowledgeFirst, result.Mode);
    }

    [Fact]
    public void Parse_ShouldUseLlmOnly_WhenValueIsLlmOnly()
    {
        var result = AiReplyModePolicy.Parse("LlmOnly");

        Assert.True(result.IsValid);
        Assert.Equal(AiReplyMode.LlmOnly, result.Mode);
    }

    [Theory]
    [InlineData("AnythingElse")]
    [InlineData("3")]
    public void Parse_ShouldFallbackToKnowledgeFirst_WhenValueIsInvalid(string value)
    {
        var result = AiReplyModePolicy.Parse(value);

        Assert.False(result.IsValid);
        Assert.Equal(AiReplyMode.KnowledgeFirst, result.Mode);
    }

    [Theory]
    [InlineData("我是Hzq", "你好呀 Hzq，很高兴认识你")]
    [InlineData("专业哥没毛病", "哈哈，谢谢认可")]
    public void EvaluateLlmOnlyAutoSend_ShouldAllowLightConversation(string question, string reply)
    {
        var decision = AiReplyModePolicy.EvaluateLlmOnlyAutoSend(question, reply);

        Assert.True(decision.IsAllowed, decision.FailureReason);
    }

    [Theory]
    [InlineData("测试商品多少钱", "这个需要人工确认一下")]
    [InlineData("今天能发货吗", "可以今天发货")]
    [InlineData("质量问题怎么赔付", "我们可以赔付")]
    public void EvaluateLlmOnlyAutoSend_ShouldBlockBusinessFactOrCommitment(string question, string reply)
    {
        var decision = AiReplyModePolicy.EvaluateLlmOnlyAutoSend(question, reply);

        Assert.False(decision.IsAllowed);
        Assert.Contains("业务事实", decision.FailureReason);
    }

    [Fact]
    public void EvaluateLlmOnlyAutoSend_ShouldAllowBusinessFact_WhenGuardDisabledForTest()
    {
        var decision = AiReplyModePolicy.EvaluateLlmOnlyAutoSend(
            "能帮我推荐一下适合夏天穿的子么",
            "当然可以！夏天适合穿透气的衣服。",
            enableBusinessFactGuard: false);

        Assert.True(decision.IsAllowed, decision.FailureReason);
    }

    [Fact]
    public void EvaluateLlmOnlyAutoSend_ShouldAllowSafeCapabilityBoundaryReply()
    {
        var decision = AiReplyModePolicy.EvaluateLlmOnlyAutoSend(
            "我需要你帮我查询一下实时天气",
            "我这边不能直接查询实时天气，建议您查看天气 App 哦。");

        Assert.True(decision.IsAllowed, decision.FailureReason);
    }

    [Fact]
    public void CanOverrideModelAutoSendForSafeCapabilityBoundary_ShouldAllowWeatherBoundaryReply()
    {
        Assert.True(AiReplyModePolicy.CanOverrideModelAutoSendForSafeCapabilityBoundary(
            "\u6211\u9700\u8981\u4f60\u5e2e\u6211\u67e5\u8be2\u4e00\u4e0b\u5b9e\u65f6\u5929\u6c14",
            "\u60a8\u53ef\u4ee5\u901a\u8fc7\u6c14\u8c61\u7f51\u7ad9\u6216 APP \u67e5\u770b\u9752\u5c9b\u7684\u5b9e\u65f6\u5929\u6c14\u60c5\u51b5\u54e6\u3002"));
    }

    [Fact]
    public void CanOverrideModelAutoSendForSafeCapabilityBoundary_ShouldBlockUnsafeLookupClaim()
    {
        Assert.False(AiReplyModePolicy.CanOverrideModelAutoSendForSafeCapabilityBoundary(
            "\u6211\u9700\u8981\u4f60\u5e2e\u6211\u67e5\u8be2\u4e00\u4e0b\u5b9e\u65f6\u5929\u6c14",
            "\u6211\u5e2e\u60a8\u67e5\u4e00\u4e0b\u9752\u5c9b\u7684\u5b9e\u65f6\u5929\u6c14\u3002"));
    }

    [Fact]
    public void IsSafeNoKnowledgeFallbackReply_ShouldKeepShortFallbackRule()
    {
        Assert.True(AiReplyModePolicy.IsSafeNoKnowledgeFallbackReply("嗯嗯好的"));
        Assert.False(AiReplyModePolicy.IsSafeNoKnowledgeFallbackReply("这个商品价格是 99 元"));
    }

    [Fact]
    public void IsSafeNoKnowledgeFallbackReply_ShouldAllowShortCapabilityBoundaryReply()
    {
        Assert.True(AiReplyModePolicy.IsSafeNoKnowledgeFallbackReply("我这边不能直接查询实时天气，建议您查看天气 App 哦。"));
    }
}
