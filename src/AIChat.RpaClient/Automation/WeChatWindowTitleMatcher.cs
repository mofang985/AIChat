namespace AIChat.RpaClient.Automation;

public static class WeChatWindowTitleMatcher
{
    public static int GetMatchScore(string title, string keyword)
    {
        if (title.Equals(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (keyword.Equals("微信", StringComparison.OrdinalIgnoreCase) &&
            title.Contains("企业微信", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ? 50 : 0;
    }

    public static int GetProcessFallbackScore(string? processName, string keyword)
    {
        if (!keyword.Equals("微信", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(processName))
        {
            return 0;
        }

        return processName.Equals("Weixin", StringComparison.OrdinalIgnoreCase) ||
            processName.Equals("WeChat", StringComparison.OrdinalIgnoreCase)
                ? 40
                : 0;
    }
}
