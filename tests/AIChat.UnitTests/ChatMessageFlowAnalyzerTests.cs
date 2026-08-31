using System.Drawing;
using AIChat.RpaClient.Automation;

namespace AIChat.UnitTests;

public sealed class ChatMessageFlowAnalyzerTests
{
    [Fact]
    public void CreateResult_ShouldReplyWhenLatestEffectiveMessageIsCustomer()
    {
        var result = ChatMessageFlowAnalyzer.CreateResult(
        [
            Message(ChatMessageSenderType.Customer, "你好", 100),
            Message(ChatMessageSenderType.Self, "您好，有什么可以帮您", 220),
            Message(ChatMessageSenderType.Customer, "你叫什么", 360)
        ],
        "Test");

        Assert.True(result.ShouldReplyLatestCustomer);
        Assert.NotNull(result.CustomerSnapshot);
        Assert.Equal("你叫什么", result.CustomerSnapshot.LatestMessage);
        Assert.NotNull(result.PendingCustomerMessageGroup);
        Assert.Single(result.PendingCustomerMessageGroup.Messages);
        Assert.Contains("客户：你叫什么", result.CustomerSnapshot.ConversationContext);
    }

    [Fact]
    public void CreateResult_ShouldGroupConsecutiveCustomerMessages()
    {
        var result = ChatMessageFlowAnalyzer.CreateResult(
        [
            Message(ChatMessageSenderType.Customer, "您好", 100),
            Message(ChatMessageSenderType.Customer, "你是谁？", 180),
            Message(ChatMessageSenderType.Customer, "你能帮我做什么？", 260)
        ],
        "Test");

        Assert.True(result.ShouldReplyLatestCustomer);
        Assert.NotNull(result.PendingCustomerMessageGroup);
        Assert.Equal(3, result.PendingCustomerMessageGroup.Messages.Count);
        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "您好",
                "你是谁？",
                "你能帮我做什么？"),
            result.CustomerSnapshot?.LatestMessage);
        Assert.Equal(0, result.PendingCustomerMessageGroup.StartOrder);
        Assert.Equal(2, result.PendingCustomerMessageGroup.EndOrder);
    }

    [Fact]
    public void CreateResult_ShouldPreserveIndividualCustomerMessagesInGroup()
    {
        var result = ChatMessageFlowAnalyzer.CreateResult(
        [
            Message(ChatMessageSenderType.Customer, "你好", 640),
            Message(ChatMessageSenderType.Customer, "你是谁？", 710),
            Message(ChatMessageSenderType.Customer, "你能帮我做什么？", 780)
        ],
        "Test");

        Assert.True(result.ShouldReplyLatestCustomer);
        Assert.Equal(3, result.PendingCustomerMessageGroup?.Messages.Count);
        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "你好",
                "你是谁？",
                "你能帮我做什么？"),
            result.PendingCustomerMessageGroup?.QuestionText);
    }

    [Fact]
    public void CreateResult_ShouldIgnoreDuplicateCustomerCropWhenTextIsContained()
    {
        var result = ChatMessageFlowAnalyzer.CreateResult(
        [
            Message(ChatMessageSenderType.Customer, "不要给我讲笑话了", 520),
            Message(ChatMessageSenderType.Self, "好的，那我就不给您讲笑话了。", 580),
            Message(
                ChatMessageSenderType.Customer,
                "我现在有几个问题，我喜欢打篮球，我应该穿什么鞋？",
                new Rectangle(20, 640, 520, 42)),
            Message(
                ChatMessageSenderType.Customer,
                "问题，我喜欢打篮球，我应该穿什么鞋？",
                new Rectangle(96, 642, 460, 38))
        ],
        "Test");

        Assert.True(result.ShouldReplyLatestCustomer);
        Assert.NotNull(result.PendingCustomerMessageGroup);
        Assert.Single(result.PendingCustomerMessageGroup.Messages);
        Assert.Equal("我现在有几个问题，我喜欢打篮球，我应该穿什么鞋？", result.PendingCustomerMessageGroup.QuestionText);
    }

    [Fact]
    public void CreateResult_ShouldNotReplyWhenLatestEffectiveMessageIsSelf()
    {
        var result = ChatMessageFlowAnalyzer.CreateResult(
        [
            Message(ChatMessageSenderType.Customer, "你好", 100),
            Message(ChatMessageSenderType.Self, "您好，有什么可以帮您", 220)
        ],
        "Test");

        Assert.False(result.ShouldReplyLatestCustomer);
        Assert.Null(result.CustomerSnapshot);
        Assert.Null(result.PendingCustomerMessageGroup);
        Assert.Equal(ChatMessageSenderType.Self, result.LatestEffectiveMessage?.SenderType);
    }

    [Fact]
    public void CreateResult_ShouldSkipSystemMessageWhenSelectingLatest()
    {
        var result = ChatMessageFlowAnalyzer.CreateResult(
        [
            Message(ChatMessageSenderType.Customer, "你好", 100),
            Message(ChatMessageSenderType.System, "17:21", 260)
        ],
        "Test");

        Assert.True(result.ShouldReplyLatestCustomer);
        Assert.Equal("你好", result.CustomerSnapshot?.LatestMessage);
    }

    [Fact]
    public void CreateResult_ShouldMergeCustomerMessagesAcrossSystemMessages()
    {
        var result = ChatMessageFlowAnalyzer.CreateResult(
        [
            Message(ChatMessageSenderType.Customer, "您好", 100),
            Message(ChatMessageSenderType.System, "17:21", 160),
            Message(ChatMessageSenderType.Customer, "你是谁？", 220)
        ],
        "Test");

        Assert.True(result.ShouldReplyLatestCustomer);
        Assert.Equal(2, result.PendingCustomerMessageGroup?.Messages.Count);
        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "您好",
                "你是谁？"),
            result.CustomerSnapshot?.LatestMessage);
    }

    [Fact]
    public void CreateResult_ShouldNotReplyWhenLatestEffectiveMessageIsUnknown()
    {
        var result = ChatMessageFlowAnalyzer.CreateResult(
        [
            Message(ChatMessageSenderType.Customer, "你好", 100),
            Message(ChatMessageSenderType.Unknown, "无法确认发送方", 260)
        ],
        "Test");

        Assert.False(result.ShouldReplyLatestCustomer);
        Assert.Null(result.CustomerSnapshot);
        Assert.Null(result.PendingCustomerMessageGroup);
        Assert.Equal(ChatMessageSenderType.Unknown, result.LatestEffectiveMessage?.SenderType);
    }

    [Fact]
    public void CreateResult_ShouldUseTopToBottomConversationContext()
    {
        var result = ChatMessageFlowAnalyzer.CreateResult(
        [
            Message(ChatMessageSenderType.Customer, "第一句", 300),
            Message(ChatMessageSenderType.Self, "第二句", 100),
            Message(ChatMessageSenderType.Customer, "第三句", 500)
        ],
        "Test");

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "我方：第二句",
                "客户：第一句",
            "客户：第三句"),
            result.CustomerSnapshot?.ConversationContext);
    }

    [Fact]
    public void CreateResult_ShouldChangeGroupFingerprintWhenCustomerAddsMessage()
    {
        var first = ChatMessageFlowAnalyzer.CreateResult(
        [
            Message(ChatMessageSenderType.Customer, "您好", 100),
            Message(ChatMessageSenderType.Customer, "你是谁？", 180)
        ],
        "Test");
        var second = ChatMessageFlowAnalyzer.CreateResult(
        [
            Message(ChatMessageSenderType.Customer, "您好", 100),
            Message(ChatMessageSenderType.Customer, "你是谁？", 180),
            Message(ChatMessageSenderType.Customer, "你能帮我做什么？", 260)
        ],
        "Test");

        Assert.NotEqual(
            first.PendingCustomerMessageGroup?.Fingerprint,
            second.PendingCustomerMessageGroup?.Fingerprint);
    }

    private static ChatMessageItem Message(ChatMessageSenderType senderType, string text, int y)
    {
        return Message(senderType, text, new Rectangle(20, y, 160, 40));
    }

    private static ChatMessageItem Message(ChatMessageSenderType senderType, string text, Rectangle bounds)
    {
        return new ChatMessageItem(
            senderType,
            text,
            bounds,
            0.9m,
            0);
    }
}
