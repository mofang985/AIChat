using AIChat.RpaClient.Automation;

namespace AIChat.UnitTests;

public sealed class ContinuousConversationStateTests
{
    [Fact]
    public void Evaluate_AllowsRecognizedMessageBeforeAnyReply()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new ContinuousConversationState(TimeSpan.FromMinutes(10));
        var snapshot = CustomerMessageExtractor.ExtractSnapshot("测试商品多少钱");

        var decision = state.Evaluate(snapshot, now);

        Assert.True(decision.ShouldReply);
    }

    [Fact]
    public void Evaluate_AllowsDifferentMessageAfterReply()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new ContinuousConversationState(TimeSpan.FromMinutes(10));
        state.RecordReplySuccess(CustomerMessageExtractor.ExtractSnapshot("测试商品多少钱")!, now);

        var next = CustomerMessageExtractor.ExtractSnapshot("测试商品有库存吗");
        var decision = state.Evaluate(next, now);

        Assert.True(decision.ShouldReply);
    }

    [Fact]
    public void RecordReplySuccess_SuppressesSameMessageTemporarily()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new ContinuousConversationState(TimeSpan.FromMinutes(10));
        var snapshot = CustomerMessageExtractor.ExtractSnapshot("测试商品有库存吗")!;

        state.RecordReplySuccess(snapshot, now);
        var decision = state.Evaluate(snapshot, now.AddMinutes(2));

        Assert.False(decision.ShouldReply);
        Assert.Equal(1, state.ReplyCount);
        Assert.Equal(0, state.ConsecutiveFailureCount);
    }

    [Fact]
    public void Evaluate_AllowsSameTextAfterSuppressWindowExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new ContinuousConversationState(TimeSpan.FromMinutes(1));
        var first = CustomerMessageExtractor.ExtractSnapshot("测试商品有库存吗", "客户：测试商品有库存吗")!;
        var repeated = CustomerMessageExtractor.ExtractSnapshot("测试商品有库存吗", "客户：测试商品有库存吗\n客户：测试商品有库存吗")!;

        state.RecordReplySuccess(first, now);
        var decision = state.Evaluate(repeated, now.AddMinutes(2));

        Assert.True(decision.ShouldReply);
    }

    [Fact]
    public void RecordReplyFailure_IncrementsConsecutiveFailureCount()
    {
        var state = new ContinuousConversationState(TimeSpan.FromMinutes(10));

        state.RecordReplyFailure();
        state.RecordReplyFailure();

        Assert.Equal(2, state.ConsecutiveFailureCount);
        Assert.True(state.HasReachedMaxFailures(2));
    }

    [Fact]
    public void RecordReplySkipped_SuppressesSameFingerprintWithoutCountingFailure()
    {
        var state = new ContinuousConversationState(TimeSpan.FromMinutes(10));
        var snapshot = CustomerMessageExtractor.ExtractSnapshot("你好")!;

        state.RecordReplyFailure();
        state.RecordReplySkipped(snapshot);
        var decision = state.Evaluate(snapshot, DateTimeOffset.UtcNow);

        Assert.False(decision.ShouldReply);
        Assert.Equal(0, state.ConsecutiveFailureCount);
    }

    [Fact]
    public void HasReachedSessionDeadline_UsesConfiguredMinutes()
    {
        var startedAt = DateTimeOffset.UtcNow;

        Assert.False(ContinuousConversationState.HasReachedSessionDeadline(startedAt, startedAt.AddMinutes(29), 30));
        Assert.True(ContinuousConversationState.HasReachedSessionDeadline(startedAt, startedAt.AddMinutes(30), 30));
    }
}
