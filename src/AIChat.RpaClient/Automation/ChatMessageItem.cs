using System.Drawing;
using System.Security.Cryptography;
using System.Text;

namespace AIChat.RpaClient.Automation;

public enum ChatMessageSenderType
{
    Customer,
    Self,
    System,
    Unknown
}

public sealed record ChatMessageItem(
    ChatMessageSenderType SenderType,
    string Text,
    Rectangle Bounds,
    decimal OcrConfidence,
    int VisualOrder,
    string OcrSource = "")
{
    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    public string SenderDisplayName => SenderType switch
    {
        ChatMessageSenderType.Customer => "客户",
        ChatMessageSenderType.Self => "我方",
        ChatMessageSenderType.System => "系统",
        _ => "未知"
    };
}

public sealed record CustomerMessageGroup(
    IReadOnlyList<ChatMessageItem> Messages,
    string QuestionText,
    string ConversationContext,
    string Fingerprint,
    int StartOrder,
    int EndOrder)
{
    public bool HasMessages =>
        Messages.Count > 0 &&
        !string.IsNullOrWhiteSpace(QuestionText);
}

public sealed record ChatMessageVisualExtractionResult(
    IReadOnlyList<ChatMessageItem> Messages,
    ChatMessageItem? LatestEffectiveMessage,
    CustomerMessageGroup? PendingCustomerMessageGroup,
    CustomerMessageSnapshot? CustomerSnapshot,
    decimal OcrConfidence,
    string Source,
    string? DebugCapturePath)
{
    public bool ShouldReplyLatestCustomer =>
        LatestEffectiveMessage?.SenderType == ChatMessageSenderType.Customer &&
        PendingCustomerMessageGroup is not null &&
        CustomerSnapshot is not null;

    public string Summary
    {
        get
        {
            var latestText = LatestEffectiveMessage is null
                ? "无"
                : $"{LatestEffectiveMessage.SenderDisplayName}：{LatestEffectiveMessage.Text}";
            var groupText = PendingCustomerMessageGroup is null
                ? "无"
                : $"{PendingCustomerMessageGroup.Messages.Count} 条";
            return $"消息数={Messages.Count}，最新={latestText}，待回复客户组={groupText}，来源={Source}，置信度={OcrConfidence:0.0000}";
        }
    }
}

public static class ChatMessageFlowAnalyzer
{
    public static ChatMessageVisualExtractionResult CreateResult(
        IReadOnlyList<ChatMessageItem> messages,
        string source,
        string? debugCapturePath = null)
    {
        var orderedMessages = DeduplicateVisualDuplicateMessages(
            messages
                .Where(message => message.HasText)
                .OrderBy(message => message.Bounds.Top)
                .ThenBy(message => message.Bounds.Left))
            .Select((message, index) => message with { VisualOrder = index })
            .ToArray();
        var latestEffectiveMessage = GetLatestEffectiveMessage(orderedMessages);
        var pendingCustomerGroup = latestEffectiveMessage?.SenderType == ChatMessageSenderType.Customer
            ? CreatePendingCustomerMessageGroup(orderedMessages, latestEffectiveMessage)
            : null;
        var customerSnapshot = pendingCustomerGroup is not null
            ? CreateCustomerSnapshot(pendingCustomerGroup)
            : null;
        var confidence = CalculateConfidence(orderedMessages);

        return new ChatMessageVisualExtractionResult(
            orderedMessages,
            latestEffectiveMessage,
            pendingCustomerGroup,
            customerSnapshot,
            confidence,
            source,
            debugCapturePath);
    }

    private static IReadOnlyList<ChatMessageItem> DeduplicateVisualDuplicateMessages(IEnumerable<ChatMessageItem> messages)
    {
        var deduplicated = new List<ChatMessageItem>();
        foreach (var message in messages)
        {
            var duplicateIndex = deduplicated.FindIndex(existing => LooksLikeDuplicateVisualMessage(existing, message));
            if (duplicateIndex < 0)
            {
                deduplicated.Add(message);
                continue;
            }

            deduplicated[duplicateIndex] = SelectMoreCompleteVisualMessage(deduplicated[duplicateIndex], message);
        }

        return deduplicated;
    }

    private static bool LooksLikeDuplicateVisualMessage(ChatMessageItem first, ChatMessageItem second)
    {
        if (first.SenderType != ChatMessageSenderType.Customer ||
            second.SenderType != ChatMessageSenderType.Customer)
        {
            return false;
        }

        if (!LooksLikeSameVisualBubble(first.Bounds, second.Bounds))
        {
            return false;
        }

        return IsContainedDuplicateText(first.Text, second.Text);
    }

    private static bool LooksLikeSameVisualBubble(Rectangle first, Rectangle second)
    {
        var minHeight = Math.Min(first.Height, second.Height);
        if (minHeight <= 0)
        {
            return false;
        }

        var verticalOverlap = Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top);
        if (verticalOverlap < minHeight * 0.72d)
        {
            return false;
        }

        var horizontalOverlap = Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left);
        return horizontalOverlap > 0;
    }

    private static bool IsContainedDuplicateText(string first, string second)
    {
        var firstNormalized = CustomerMessageExtractor.NormalizeForComparison(first);
        var secondNormalized = CustomerMessageExtractor.NormalizeForComparison(second);
        if (string.IsNullOrWhiteSpace(firstNormalized) ||
            string.IsNullOrWhiteSpace(secondNormalized))
        {
            return false;
        }

        if (string.Equals(firstNormalized, secondNormalized, StringComparison.Ordinal))
        {
            return firstNormalized.Length >= 2;
        }

        var shorter = firstNormalized.Length <= secondNormalized.Length
            ? firstNormalized
            : secondNormalized;
        var longer = firstNormalized.Length > secondNormalized.Length
            ? firstNormalized
            : secondNormalized;

        return shorter.Length >= 4 && longer.Contains(shorter, StringComparison.Ordinal);
    }

    private static ChatMessageItem SelectMoreCompleteVisualMessage(ChatMessageItem first, ChatMessageItem second)
    {
        var firstNormalized = CustomerMessageExtractor.NormalizeForComparison(first.Text);
        var secondNormalized = CustomerMessageExtractor.NormalizeForComparison(second.Text);
        if (secondNormalized.Length > firstNormalized.Length)
        {
            return second;
        }

        if (secondNormalized.Length == firstNormalized.Length &&
            second.OcrConfidence > first.OcrConfidence)
        {
            return second;
        }

        return first;
    }

    public static ChatMessageItem? GetLatestEffectiveMessage(IEnumerable<ChatMessageItem> messages)
    {
        return messages
            .Where(IsEffectiveMessage)
            .OrderByDescending(message => message.Bounds.Bottom)
            .ThenByDescending(message => message.VisualOrder)
            .FirstOrDefault();
    }

    public static string FormatConversationContext(IEnumerable<ChatMessageItem> messages)
    {
        var lines = messages
            .Where(message => message.HasText)
            .Where(message => message.SenderType is ChatMessageSenderType.Customer or ChatMessageSenderType.Self)
            .OrderBy(message => message.VisualOrder)
            .Select(message => $"{message.SenderDisplayName}：{message.Text.Trim()}")
            .ToArray();

        return string.Join(Environment.NewLine, lines);
    }

    public static string CreateFingerprint(string latestMessage, string conversationContext, int messageCount)
    {
        var normalizedLatest = CustomerMessageExtractor.NormalizeForComparison(latestMessage);
        var normalizedContext = CustomerMessageExtractor.NormalizeForComparison(conversationContext);
        if (normalizedContext.Length > 800)
        {
            normalizedContext = normalizedContext[^800..];
        }

        var raw = $"{normalizedLatest}|{normalizedContext}|{messageCount}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..24].ToLowerInvariant();
    }

    private static CustomerMessageGroup? CreatePendingCustomerMessageGroup(
        IReadOnlyList<ChatMessageItem> messages,
        ChatMessageItem latestCustomerMessage)
    {
        var latestIndex = messages
            .Select((message, index) => new { Message = message, Index = index })
            .LastOrDefault(item => item.Message.VisualOrder == latestCustomerMessage.VisualOrder)
            ?.Index ?? -1;
        if (latestIndex < 0)
        {
            return null;
        }

        var groupMessages = new List<ChatMessageItem>();
        for (var index = latestIndex; index >= 0; index--)
        {
            var message = messages[index];
            if (!IsEffectiveMessage(message))
            {
                continue;
            }

            if (message.SenderType == ChatMessageSenderType.Customer)
            {
                groupMessages.Add(message);
                continue;
            }

            break;
        }

        groupMessages.Reverse();
        if (groupMessages.Count == 0)
        {
            return null;
        }

        var context = FormatConversationContext(messages);
        if (string.IsNullOrWhiteSpace(context))
        {
            context = string.Join(Environment.NewLine, groupMessages.Select(message => message.Text.Trim()));
        }

        var questionText = string.Join(
            Environment.NewLine,
            groupMessages
                .Select(message => message.Text.Trim())
                .Where(text => !string.IsNullOrWhiteSpace(text)));
        if (string.IsNullOrWhiteSpace(questionText))
        {
            return null;
        }

        var fingerprint = CreateFingerprint(questionText, context, groupMessages.Count);
        return new CustomerMessageGroup(
            groupMessages,
            questionText,
            context,
            fingerprint,
            groupMessages.First().VisualOrder,
            groupMessages.Last().VisualOrder);
    }

    private static CustomerMessageSnapshot? CreateCustomerSnapshot(CustomerMessageGroup group)
    {
        if (!group.HasMessages)
        {
            return null;
        }

        return new CustomerMessageSnapshot(
            group.QuestionText,
            group.ConversationContext,
            group.Fingerprint,
            group.Messages.Count,
            false);
    }

    private static bool IsEffectiveMessage(ChatMessageItem message)
    {
        if (!message.HasText)
        {
            return false;
        }

        if (message.SenderType == ChatMessageSenderType.System)
        {
            return false;
        }

        return !CustomerMessageExtractor.IsWeChatSystemNotice(message.Text);
    }

    private static decimal CalculateConfidence(IReadOnlyList<ChatMessageItem> messages)
    {
        var values = messages
            .Where(message => message.HasText)
            .Select(message => message.OcrConfidence)
            .Where(confidence => confidence > 0)
            .ToArray();

        return values.Length == 0 ? 0m : values.Average();
    }
}
