using AIChat.Application.Knowledge;
using AIChat.Domain.Enums;

namespace AIChat.UnitTests;

public sealed class KeywordKnowledgeSearchServiceTests
{
    private readonly KeywordKnowledgeSearchService _service = new();

    [Fact]
    public void Search_ShouldReturnHits_WhenKeywordMatches()
    {
        var ruleId = Guid.NewGuid();
        var results = _service.Search(
            "退货",
            [
                new KnowledgeSearchCandidate(
                    KnowledgeSourceType.AfterSaleRule,
                    ruleId,
                    "退货规则",
                    "支持七天无理由退货，具体以商品页面说明为准。",
                    "退货 售后",
                    20),
                new KnowledgeSearchCandidate(
                    KnowledgeSourceType.Product,
                    Guid.NewGuid(),
                    "保温杯",
                    "316 不锈钢内胆。",
                    "杯子 水杯",
                    100)
            ]);

        Assert.Single(results);
        Assert.Equal(ruleId, results[0].SourceId);
        Assert.True(results[0].Score > 0);
    }

    [Fact]
    public void Search_ShouldReturnEmpty_WhenNothingMatches()
    {
        var results = _service.Search(
            "发票",
            [
                new KnowledgeSearchCandidate(
                    KnowledgeSourceType.Faq,
                    Guid.NewGuid(),
                    "物流时效",
                    "默认 48 小时内发货。",
                    "物流 发货",
                    100)
            ]);

        Assert.Empty(results);
    }

    [Fact]
    public void Search_ShouldOrderByScoreThenPriority()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();

        var results = _service.Search(
            "保修",
            [
                new KnowledgeSearchCandidate(KnowledgeSourceType.Faq, secondId, "保修说明", "提供保修服务。", null, 50),
                new KnowledgeSearchCandidate(KnowledgeSourceType.Faq, firstId, "保修政策", "提供保修服务。", null, 10),
                new KnowledgeSearchCandidate(KnowledgeSourceType.Product, thirdId, "高端电器", "保修期以说明书为准。", "保修", 100)
            ],
            maxResults: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal(thirdId, results[0].SourceId);
        Assert.Equal(firstId, results[1].SourceId);
    }
}
