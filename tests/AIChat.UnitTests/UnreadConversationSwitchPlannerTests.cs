using System.Drawing;
using AIChat.RpaClient.Automation;

namespace AIChat.UnitTests;

public sealed class UnreadConversationSwitchPlannerTests
{
    [Fact]
    public void FindFirstSwitchableCandidate_ShouldReturnFirstStableNamedCandidate()
    {
        var snapshot = new UnreadConversationQueueSnapshot(
            DateTimeOffset.Parse("2026-08-13T10:00:00Z"),
            new Rectangle(0, 0, 272, 1568),
            [
                CreateCandidate(0, "观察中", isStable: false, name: "琦琦"),
                CreateCandidate(1, "可切换候选", isStable: true, name: "贺志奇"),
                CreateCandidate(2, "可切换候选", isStable: true, name: "盛安德-崔萌")
            ],
            "扫描完成。",
            null);

        var target = UnreadConversationSwitchPlanner.FindFirstSwitchableCandidate(snapshot);

        Assert.NotNull(target);
        Assert.Equal("贺志奇", target.TextInfo?.ConversationName);
    }

    [Fact]
    public void ValidateSwitchTarget_ShouldBlockUnstableCandidate()
    {
        var candidate = CreateCandidate(0, "观察中", isStable: false, name: "贺志奇");

        var validation = UnreadConversationSwitchPlanner.ValidateSwitchTarget(candidate, new Rectangle(0, 0, 272, 1568));

        Assert.False(validation.IsAllowed);
        Assert.Contains("稳定性预演", validation.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateSwitchTarget_ShouldBlockCandidateWithoutName()
    {
        var candidate = CreateCandidate(0, "可切换候选", isStable: true, name: string.Empty);

        var validation = UnreadConversationSwitchPlanner.ValidateSwitchTarget(candidate, new Rectangle(0, 0, 272, 1568));

        Assert.False(validation.IsAllowed);
        Assert.Contains("可靠会话名", validation.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateSwitchTarget_ShouldBlockCandidateOutsideConversationList()
    {
        var candidate = CreateCandidate(0, "可切换候选", isStable: true, name: "贺志奇", rowTop: 1600);

        var validation = UnreadConversationSwitchPlanner.ValidateSwitchTarget(candidate, new Rectangle(0, 0, 272, 1568));

        Assert.False(validation.IsAllowed);
        Assert.Contains("会话列表区域", validation.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateSwitchTarget_ShouldAllowStableNamedCandidateInsideList()
    {
        var candidate = CreateCandidate(0, "可切换候选", isStable: true, name: "贺志奇");

        var validation = UnreadConversationSwitchPlanner.ValidateSwitchTarget(candidate, new Rectangle(0, 0, 272, 1568));

        Assert.True(validation.IsAllowed);
        Assert.Contains("前置校验", validation.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateClickPoint_ShouldClickInsideConversationRow()
    {
        var point = UnreadConversationSwitchPlanner.CreateClickPoint(new Rectangle(3840, 516, 272, 84));

        Assert.Equal(new Point(3981, 558), point);
    }

    [Fact]
    public void CreateTitleRegion_ShouldUseChatHeaderArea()
    {
        var titleRegion = UnreadConversationSwitchPlanner.CreateTitleRegion(
            new Rectangle(0, 0, 1568, 850),
            new Rectangle(185, 78, 1383, 650));

        Assert.Equal(new Rectangle(202, 0, 691, 78), titleRegion);
    }

    [Theory]
    [InlineData("贺志奇", "贺志奇", true)]
    [InlineData("贺志奇（客户）", "贺志奇", true)]
    [InlineData("贺志", "贺志奇", true)]
    [InlineData("盛安德-崔萌", "盛安德-崔萌", true)]
    [InlineData("盛安德同桌", "贺志奇", false)]
    [InlineData("", "贺志奇", false)]
    public void TitleMatchesTarget_ShouldMatchNormalizedConversationName(string titleOcrText, string targetName, bool expected)
    {
        Assert.Equal(expected, UnreadConversationSwitchPlanner.TitleMatchesTarget(titleOcrText, targetName));
    }

    [Fact]
    public void CreateMessageVerifyRegion_ShouldUseBottomConversationContext()
    {
        var region = UnreadConversationSwitchPlanner.CreateMessageVerifyRegion(
            new Rectangle(185, 78, 1383, 650),
            0.60m);

        Assert.Equal(new Rectangle(185, 338, 1383, 390), region);
    }

    [Fact]
    public void MessageVerifier_ShouldPassWhenLatestVisibleMessageContainsQueuePreview()
    {
        var candidate = CreateCandidate(0, "可切换候选", isStable: true, name: "贺志奇", preview: "还特别的吗");
        var visual = CreateVisualResult(
            "昨天",
            "您好",
            "还特别的吗？");

        var verification = UnreadConversationMessageVerifier.Verify(candidate, visual, 2);

        Assert.True(verification.IsVerified);
        Assert.False(verification.IsSkipped);
        Assert.Contains("最新可见消息", verification.Reason, StringComparison.Ordinal);
        Assert.Equal("还特别的吗？", verification.MatchedText);
    }

    [Fact]
    public void MessageVerifier_ShouldSkipWhenQueuePreviewHasNoComparableText()
    {
        var candidate = CreateCandidate(0, "可切换候选", isStable: true, name: "贺志奇", preview: "...");
        var visual = CreateVisualResult("右侧最新消息");

        var verification = UnreadConversationMessageVerifier.Verify(candidate, visual, 2);

        Assert.False(verification.IsVerified);
        Assert.True(verification.IsSkipped);
        Assert.False(verification.IsBlockingFailure);
        Assert.Contains("缺少可比较文本", verification.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void MessageVerifier_ShouldBlockWhenOnlyOlderVisibleMessageMatchesPreview()
    {
        var candidate = CreateCandidate(0, "可切换候选", isStable: true, name: "贺志奇", preview: "目标摘要");
        var visual = CreateVisualResult(
            "目标摘要",
            "中间消息一",
            "中间消息二",
            "新的消息");

        var verification = UnreadConversationMessageVerifier.Verify(candidate, visual, 2);

        Assert.False(verification.IsVerified);
        Assert.False(verification.IsSkipped);
        Assert.True(verification.IsBlockingFailure);
        Assert.Contains("较早可见消息", verification.Reason, StringComparison.Ordinal);
        Assert.Equal("目标摘要", verification.MatchedText);
    }


    [Fact]
    public void LooksSelectedConversationRow_ShouldDetectGreenSelectedRow()
    {
        Assert.True(UnreadConversationSwitchPlanner.LooksSelectedConversationRow(
            272,
            84,
            (_, _) => ((byte)22, (byte)177, (byte)94)));
    }

    [Fact]
    public void LooksSelectedConversationRow_ShouldRejectNormalWhiteRow()
    {
        Assert.False(UnreadConversationSwitchPlanner.LooksSelectedConversationRow(
            272,
            84,
            (_, _) => ((byte)245, (byte)245, (byte)245)));
    }

    [Fact]
    public void SwitchResultToLogMessage_ShouldIncludeClickPointTitleAndMessageVerification()
    {
        var candidate = CreateCandidate(0, "可切换候选", isStable: true, name: "贺志奇");
        var result = new UnreadConversationSwitchResult(
            true,
            "切换成功",
            "右侧聊天标题校验通过，未输入、未发送。",
            candidate,
            new Point(3981, 558),
            new Rectangle(4200, 0, 480, 70),
            "贺志奇",
            new UnreadConversationMessageVerification(
                true,
                false,
                "摘要校验通过",
                "右侧最新可见消息包含队列摘要",
                "还特别的吗",
                "还特别的吗？",
                "Test",
                "message.png"));

        var message = result.ToLogMessage();

        Assert.Contains("切换成功", message, StringComparison.Ordinal);
        Assert.Contains("X=3981,Y=558", message, StringComparison.Ordinal);
        Assert.Contains("标题 OCR=贺志奇", message, StringComparison.Ordinal);
        Assert.Contains("摘要校验通过", message, StringComparison.Ordinal);
        Assert.Contains("队列摘要=还特别的吗", message, StringComparison.Ordinal);
    }

    private static ChatMessageVisualExtractionResult CreateVisualResult(params string[] lines)
    {
        var messages = lines
            .Select((line, index) => new ChatMessageItem(
                ChatMessageSenderType.Unknown,
                line,
                new Rectangle(0, index * 32, 240, 24),
                0.90m,
                index,
                "TestOcr"))
            .ToArray();
        return ChatMessageFlowAnalyzer.CreateResult(messages, "TestVisual", "message.png");
    }


    private static UnreadConversationCandidate CreateCandidate(
        int visualOrder,
        string preflightStatus,
        bool isStable,
        string name,
        int rowTop = 516,
        string preview = "还特别的吗")
    {
        return new UnreadConversationCandidate(
            VisualOrder: visualOrder,
            BadgeBounds: new Rectangle(53, rowTop + 18, 18, 18),
            RowBounds: new Rectangle(0, rowTop, 272, 84),
            UnreadHint: "数字未读候选",
            Confidence: 0.90m,
            Source: "Test",
            TextInfo: new UnreadConversationTextInfo(name, preview, "15:32", "1", 0.86m, "TestOcr", "raw"),
            Preflight: new UnreadConversationReadOnlyPreflight(
                isStable,
                preflightStatus,
                isStable ? "连续 2 次扫描一致" : "还需 1 次一致扫描",
                isStable ? 2 : 1,
                2,
                $"{name}｜1｜{preview}｜15:32",
                DateTimeOffset.Parse("2026-08-13T10:00:00Z"),
                DateTimeOffset.Parse("2026-08-13T10:00:06Z")));
    }
}
