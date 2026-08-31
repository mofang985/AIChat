using AIChat.RpaClient.Automation;

namespace AIChat.UnitTests;

public sealed class CustomerMessageTextCorrectorTests
{
    [Fact]
    public void Correct_ShouldFixKnownShortPhraseOcrMistake()
    {
        var corrected = CustomerMessageTextCorrector.Correct("好的，我首了，谢谢你");

        Assert.Equal("好的，我知道了，谢谢你", corrected);
    }

    [Fact]
    public void Correct_ShouldKeepUnrelatedText()
    {
        var corrected = CustomerMessageTextCorrector.Correct("我想问一下30度穿什么衣服");

        Assert.Equal("我想问一下30度穿什么衣服", corrected);
    }

    [Fact]
    public void Correct_ShouldFixShortHelloMistake()
    {
        var corrected = CustomerMessageTextCorrector.Correct("1 子");

        Assert.Equal("你好", corrected);
    }

    [Fact]
    public void Correct_ShouldFixQuestionMarkMistake()
    {
        var corrected = CustomerMessageTextCorrector.Correct("你能帮我做什么 7");

        Assert.Equal("你能帮我做什么？", corrected);
    }
}
