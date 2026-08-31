using AIChat.RpaClient.Automation;

namespace AIChat.UnitTests;

public sealed class OcrResultSelectorTests
{
    [Fact]
    public void ChooseBetter_ShouldPreferMoreCompleteSimilarChineseText()
    {
        var windows = new OcrResult("子兄弟不是衣服", 0.90m, "WindowsOcr");
        var paddle = new OcrResult("鞋子 兄弟 不是衣服", 0.82m, "FocusedCrop");

        var selected = OcrResultSelector.ChooseBetter(windows, paddle);

        Assert.Equal("鞋子 兄弟 不是衣服", selected.Text);
    }

    [Fact]
    public void ChooseBetter_ShouldKeepHigherQualityWhenLongerTextIsUnrelated()
    {
        var windows = new OcrResult("你好", 0.90m, "WindowsOcr");
        var paddle = new OcrResult("商品价格库存物流", 0.82m, "FocusedCrop");

        var selected = OcrResultSelector.ChooseBetter(windows, paddle);

        Assert.Equal("你好", selected.Text);
    }
}
