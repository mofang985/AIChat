using AIChat.Domain.Entities;

namespace AIChat.Application.RpaTasks;

public sealed class RpaTaskResultUpdater
{
    public void Apply(RpaTask task, RpaTaskResultUpdate update)
    {
        task.ConversationKey = KeepExistingWhenBlank(task.ConversationKey, update.ConversationKey);
        task.CustomerDisplayName = KeepExistingWhenBlank(task.CustomerDisplayName, update.CustomerDisplayName);
        task.IncomingMessageText = KeepExistingWhenBlank(task.IncomingMessageText, update.IncomingMessageText);
        task.AiReplyText = KeepExistingWhenBlank(task.AiReplyText, update.AiReplyText);
        task.RiskResult = KeepExistingWhenBlank(task.RiskResult, update.RiskResult);
    }

    private static string? KeepExistingWhenBlank(string? currentValue, string? incomingValue)
    {
        return string.IsNullOrWhiteSpace(incomingValue) ? currentValue : incomingValue.Trim();
    }
}

public sealed record RpaTaskResultUpdate(
    string? ConversationKey,
    string? CustomerDisplayName,
    string? IncomingMessageText,
    string? AiReplyText,
    string? RiskResult);
