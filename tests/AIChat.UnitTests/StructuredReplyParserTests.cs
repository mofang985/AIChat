using AIChat.Application.AI;
using AIChat.Domain.Enums;

namespace AIChat.UnitTests;

public sealed class StructuredReplyParserTests
{
    private readonly StructuredReplyParser _parser = new();

    [Fact]
    public void TryParse_ShouldReturnStructuredOutput_WhenJsonIsValid()
    {
        var raw = """
            ```json
            {
              "Intent": "ProductInquiry",
              "Confidence": 0.92,
              "RiskLevel": "Low",
              "ReplyText": "您好，这款商品支持正常发货。",
              "KnowledgeRefs": ["faq-1"],
              "ShouldAutoSend": true
            }
            ```
            """;

        var ok = _parser.TryParseReply(raw, out var output, out var errorMessage);

        Assert.True(ok, errorMessage);
        Assert.Equal("ProductInquiry", output.Intent);
        Assert.Equal(0.92m, output.Confidence);
        Assert.Equal(RiskLevel.Low, output.RiskLevel);
        Assert.True(output.ShouldAutoSend);
        Assert.Equal("faq-1", output.KnowledgeRefs.Single());
    }

    [Fact]
    public void TryParse_ShouldFail_WhenJsonIsInvalid()
    {
        var ok = _parser.TryParseReply("不是 JSON", out var output, out var errorMessage);

        Assert.False(ok);
        Assert.Equal(RiskLevel.High, output.RiskLevel);
        Assert.Contains("not valid JSON", errorMessage);
    }

    [Fact]
    public void TryParse_ShouldFail_WhenRequiredFieldsMissing()
    {
        var ok = _parser.TryParseReply(
            """{"Intent":"ProductInquiry","RiskLevel":"Low","ShouldAutoSend":true}""",
            out _,
            out var errorMessage);

        Assert.False(ok);
        Assert.Contains("Intent and ReplyText", errorMessage);
    }
}
