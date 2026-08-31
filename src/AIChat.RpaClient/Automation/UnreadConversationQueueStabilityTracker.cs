using AIChat.RpaClient.Configuration;

namespace AIChat.RpaClient.Automation;

public sealed record UnreadConversationReadOnlyPreflight(
    bool IsStable,
    string StatusText,
    string Reason,
    int StableScanCount,
    int RequiredStableScanCount,
    string Fingerprint,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastSeenAtUtc)
{
    public string ToDisplayText()
    {
        return $"{StatusText}({StableScanCount}/{RequiredStableScanCount})：{Reason}";
    }
}

internal sealed class UnreadConversationQueueStabilityTracker
{
    private readonly Dictionary<string, StableEntry> _entries = new(StringComparer.Ordinal);

    public UnreadConversationQueueSnapshot Apply(UnreadConversationQueueSnapshot snapshot, RpaAutomationOptions options)
    {
        if (!options.EnableUnreadQueueReadOnlyPreflight)
        {
            return snapshot;
        }

        if (snapshot.Candidates.Count == 0)
        {
            _entries.Clear();
            return snapshot with { Summary = $"{snapshot.Summary} 预演：无可切换候选。" };
        }

        var requiredStableScanCount = Math.Max(2, options.UnreadQueueRequiredStableScanCount);
        var rowTolerancePixels = Math.Max(0, options.UnreadQueueStableRowTolerancePixels);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var stableCount = 0;
        var pendingCount = 0;
        var enriched = new List<UnreadConversationCandidate>(snapshot.Candidates.Count);

        foreach (var candidate in snapshot.Candidates)
        {
            var fingerprint = UnreadConversationCandidateFingerprint.Create(candidate);
            if (!fingerprint.HasRequiredText)
            {
                pendingCount++;
                enriched.Add(candidate with
                {
                    Preflight = new UnreadConversationReadOnlyPreflight(
                        false,
                        "观察中",
                        "OCR 文本为空，仅观察角标位置，不作为可切换候选",
                        0,
                        requiredStableScanCount,
                        fingerprint.DisplayText,
                        snapshot.ScannedAtUtc,
                        snapshot.ScannedAtUtc)
                });
                continue;
            }

            seenKeys.Add(fingerprint.Key);
            if (!_entries.TryGetValue(fingerprint.Key, out var entry) ||
                Math.Abs(entry.LastRowCenterY - fingerprint.RowCenterY) > rowTolerancePixels)
            {
                entry = new StableEntry(snapshot.ScannedAtUtc, snapshot.ScannedAtUtc, fingerprint.RowCenterY, 0);
            }

            entry = entry with
            {
                LastSeenAtUtc = snapshot.ScannedAtUtc,
                LastRowCenterY = fingerprint.RowCenterY,
                StableScanCount = entry.StableScanCount + 1
            };
            _entries[fingerprint.Key] = entry;

            var isStable = entry.StableScanCount >= requiredStableScanCount;
            if (isStable)
            {
                stableCount++;
            }
            else
            {
                pendingCount++;
            }

            enriched.Add(candidate with
            {
                Preflight = new UnreadConversationReadOnlyPreflight(
                    isStable,
                    isStable ? "可切换候选" : "观察中",
                    isStable ? $"连续 {entry.StableScanCount} 次扫描一致" : $"还需 {requiredStableScanCount - entry.StableScanCount} 次一致扫描",
                    entry.StableScanCount,
                    requiredStableScanCount,
                    fingerprint.DisplayText,
                    entry.FirstSeenAtUtc,
                    entry.LastSeenAtUtc)
            });
        }

        PruneEntries(seenKeys, snapshot.ScannedAtUtc, options.UnreadQueueStabilityCacheMinutes);
        var summary = $"{snapshot.Summary} 预演：可切换候选 {stableCount} 个，观察中 {pendingCount} 个，跳过 0 个。";
        return snapshot with { Candidates = enriched, Summary = summary };
    }


    private void PruneEntries(HashSet<string> seenKeys, DateTimeOffset scannedAtUtc, int cacheMinutes)
    {
        var ttl = TimeSpan.FromMinutes(Math.Max(1, cacheMinutes));
        foreach (var key in _entries.Keys.ToArray())
        {
            if (seenKeys.Contains(key))
            {
                continue;
            }

            if (scannedAtUtc - _entries[key].LastSeenAtUtc >= ttl)
            {
                _entries.Remove(key);
            }
        }
    }

    private sealed record StableEntry(
        DateTimeOffset FirstSeenAtUtc,
        DateTimeOffset LastSeenAtUtc,
        int LastRowCenterY,
        int StableScanCount);
}

internal sealed record UnreadConversationCandidateFingerprint(
    bool HasRequiredText,
    string Key,
    string DisplayText,
    int RowCenterY)
{
    public static UnreadConversationCandidateFingerprint Create(UnreadConversationCandidate candidate)
    {
        var info = candidate.TextInfo;
        var name = Normalize(info?.ConversationName);
        var preview = Normalize(info?.LatestMessagePreview);
        var time = Normalize(info?.TimeText);
        var unreadOcr = Normalize(info?.UnreadCountText);
        var rawText = Normalize(info?.RawText);
        var unread = string.IsNullOrWhiteSpace(unreadOcr) ? "数字角标" : unreadOcr;
        var displayName = string.IsNullOrWhiteSpace(name) ? "未命名会话" : name;
        var rowCenterY = candidate.RowBounds.Top + candidate.RowBounds.Height / 2;
        var hasRequiredText = info is { HasAnyText: true };
        var key = hasRequiredText
            ? $"{displayName}|{preview}|{time}|{unread}|{rawText}"
            : $"Badge:{candidate.BadgeBounds.X},{candidate.BadgeBounds.Y},{candidate.BadgeBounds.Width},{candidate.BadgeBounds.Height}";
        var display = hasRequiredText
            ? $"{displayName}｜{unread}｜{preview}｜{time}"
            : key;
        return new UnreadConversationCandidateFingerprint(hasRequiredText, key, display, rowCenterY);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Concat(value.Where(ch => !char.IsWhiteSpace(ch))).Trim();
    }
}
