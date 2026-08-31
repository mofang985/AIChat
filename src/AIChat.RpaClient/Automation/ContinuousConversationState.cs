namespace AIChat.RpaClient.Automation;

public sealed class ContinuousConversationState(TimeSpan duplicateSuppressWindow)
{
    private readonly HashSet<string> _repliedFingerprints = new(StringComparer.Ordinal);
    private readonly HashSet<string> _skippedFingerprints = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _repliedMessageTexts = new(StringComparer.Ordinal);

    public int ReplyCount { get; private set; }
    public int ConsecutiveFailureCount { get; private set; }

    public ContinuousMessageDecision Evaluate(CustomerMessageSnapshot? snapshot, DateTimeOffset now)
    {
        if (snapshot is null || !snapshot.HasMessage)
        {
            return ContinuousMessageDecision.Skip("未识别到新的客户消息。");
        }

        PurgeExpiredMessageTexts(now);

        var normalizedMessage = CustomerMessageExtractor.NormalizeForComparison(snapshot.LatestMessage);
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            return ContinuousMessageDecision.Skip("最新消息为空。");
        }

        if (_repliedFingerprints.Contains(snapshot.Fingerprint))
        {
            return ContinuousMessageDecision.Skip("消息指纹已回复，跳过重复处理。");
        }

        if (_skippedFingerprints.Contains(snapshot.Fingerprint))
        {
            return ContinuousMessageDecision.Skip("Current message fingerprint was already skipped; waiting for a new customer message.");
        }

        if (duplicateSuppressWindow > TimeSpan.Zero && _repliedMessageTexts.ContainsKey(normalizedMessage))
        {
            return ContinuousMessageDecision.Skip("短时间内相同客户消息已回复，跳过重复处理。");
        }

        return ContinuousMessageDecision.Reply("检测到新客户消息。");
    }

    public void RecordReplySuccess(CustomerMessageSnapshot snapshot, DateTimeOffset now)
    {
        ReplyCount++;
        ConsecutiveFailureCount = 0;
        _repliedFingerprints.Add(snapshot.Fingerprint);

        var normalizedMessage = CustomerMessageExtractor.NormalizeForComparison(snapshot.LatestMessage);
        if (duplicateSuppressWindow > TimeSpan.Zero && !string.IsNullOrWhiteSpace(normalizedMessage))
        {
            _repliedMessageTexts[normalizedMessage] = now;
        }
    }

    public void RecordReplyFailure()
    {
        ConsecutiveFailureCount++;
    }

    public void RecordReplySkipped(CustomerMessageSnapshot snapshot)
    {
        ConsecutiveFailureCount = 0;
        if (!string.IsNullOrWhiteSpace(snapshot.Fingerprint))
        {
            _skippedFingerprints.Add(snapshot.Fingerprint);
        }
    }

    public bool HasReachedMaxReplies(int maxReplies)
    {
        return maxReplies > 0 && ReplyCount >= maxReplies;
    }

    public bool HasReachedMaxFailures(int maxFailures)
    {
        return maxFailures > 0 && ConsecutiveFailureCount >= maxFailures;
    }

    public static bool HasReachedSessionDeadline(DateTimeOffset startedAtUtc, DateTimeOffset nowUtc, int maxSessionMinutes)
    {
        return maxSessionMinutes > 0 && nowUtc - startedAtUtc >= TimeSpan.FromMinutes(maxSessionMinutes);
    }

    private void PurgeExpiredMessageTexts(DateTimeOffset now)
    {
        if (duplicateSuppressWindow <= TimeSpan.Zero || _repliedMessageTexts.Count == 0)
        {
            return;
        }

        var expiredKeys = _repliedMessageTexts
            .Where(item => now - item.Value > duplicateSuppressWindow)
            .Select(item => item.Key)
            .ToArray();

        foreach (var key in expiredKeys)
        {
            _repliedMessageTexts.Remove(key);
        }
    }
}

public sealed record ContinuousMessageDecision(bool ShouldReply, string Reason)
{
    public static ContinuousMessageDecision Reply(string reason)
    {
        return new ContinuousMessageDecision(true, reason);
    }

    public static ContinuousMessageDecision Skip(string reason)
    {
        return new ContinuousMessageDecision(false, reason);
    }
}
