using AIChat.RpaClient.Automation;

namespace AIChat.UnitTests;

public sealed class WeChatWindowTitleMatcherTests
{
    [Theory]
    [InlineData("微信", "微信", 100)]
    [InlineData("微信 - 测试客户", "微信", 50)]
    [InlineData("企业微信", "微信", 0)]
    [InlineData("企业微信", "企业微信", 100)]
    public void GetMatchScore_ShouldExcludeEnterpriseWeChatWhenKeywordTargetsConsumerWeChat(
        string title,
        string keyword,
        int expectedScore)
    {
        var score = WeChatWindowTitleMatcher.GetMatchScore(title, keyword);

        Assert.Equal(expectedScore, score);
    }

    [Theory]
    [InlineData("Weixin", "微信", 40)]
    [InlineData("WeChat", "微信", 40)]
    [InlineData("WXWork", "微信", 0)]
    [InlineData("Weixin", "企业微信", 0)]
    public void GetProcessFallbackScore_ShouldMatchOnlyConsumerWeChatProcessForWeChatKeyword(
        string processName,
        string keyword,
        int expectedScore)
    {
        var score = WeChatWindowTitleMatcher.GetProcessFallbackScore(processName, keyword);

        Assert.Equal(expectedScore, score);
    }
}
