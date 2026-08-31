using AIChat.Application.RpaTasks;
using AIChat.Domain.Entities;

namespace AIChat.UnitTests;

public sealed class RpaTaskResultUpdaterTests
{
    private readonly RpaTaskResultUpdater _updater = new();

    [Fact]
    public void Apply_ShouldUpdateNonBlankValues()
    {
        var task = new RpaTask();

        _updater.Apply(
            task,
            new RpaTaskResultUpdate(
                " conversation-1 ",
                " 当前会话 ",
                " 客户消息 ",
                " AI 回复 ",
                " Low "));

        Assert.Equal("conversation-1", task.ConversationKey);
        Assert.Equal("当前会话", task.CustomerDisplayName);
        Assert.Equal("客户消息", task.IncomingMessageText);
        Assert.Equal("AI 回复", task.AiReplyText);
        Assert.Equal("Low", task.RiskResult);
    }

    [Fact]
    public void Apply_ShouldKeepExistingValues_WhenIncomingValuesAreBlank()
    {
        var task = new RpaTask
        {
            ConversationKey = "conversation-1",
            CustomerDisplayName = "客户A",
            IncomingMessageText = "旧客户消息",
            AiReplyText = "旧AI回复",
            RiskResult = "Low"
        };

        _updater.Apply(
            task,
            new RpaTaskResultUpdate(
                null,
                "",
                "   ",
                null,
                ""));

        Assert.Equal("conversation-1", task.ConversationKey);
        Assert.Equal("客户A", task.CustomerDisplayName);
        Assert.Equal("旧客户消息", task.IncomingMessageText);
        Assert.Equal("旧AI回复", task.AiReplyText);
        Assert.Equal("Low", task.RiskResult);
    }
}
