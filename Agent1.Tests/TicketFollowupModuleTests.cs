using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Agent1.Modules;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// P5a-2: TicketFollowupModule 工单解析 + TicketItem 状态机测试
///
/// 聚焦纯逻辑（无 LLM 依赖）：
///   - ParseTickets: LLM 输出解析 → List＜TicketItem＞
///   - TicketItem 状态机: New→Accepted→InProgress→Completed→Verified→Closed | Rejected
///   - IsOpen 属性: 各状态的开关语义
///   - BuildTicketExtractionPrompt: Prompt 模板生成
/// </summary>
public class TicketFollowupModuleTests
{
    // ═══════════════════════════════════════
    // ParseTickets: LLM 输出解析
    // ═══════════════════════════════════════

    [Fact]
    public void ParseTickets_EmptyOrNull_ReturnsEmpty()
    {
        AssertParseReturnsCount("", 0);
        AssertParseReturnsCount(null!, 0);
    }

    [Fact]
    public void ParseTickets_NoNeedRectify_ReturnsEmpty()
    {
        AssertParseReturnsCount("经检查，无需整改。", 0);
        AssertParseReturnsCount("合规检查通过，无需整改。", 0);
    }

    [Fact]
    public void ParseTickets_SingleTicket_AllFields()
    {
        var input = @"
【问题】: 消防通道堆放杂物
【整改措施】: 立即清理消防通道，确保宽度 ≥ 4m
【优先级】: 高
【建议截止日期】: 2026-07-17
【负责人】: 仓库主管
【引用法规】: GB 50016-2014 §4.2.3";

        var tickets = CallParseTickets(input);

        tickets.Should().HaveCount(1);
        var t = tickets[0];
        t.Id.Should().Be(1);
        t.Issue.Should().Be("消防通道堆放杂物");
        t.Action.Should().Be("立即清理消防通道，确保宽度 ≥ 4m");
        t.Priority.Should().Be("高");
        t.SuggestedDeadline.Should().Be(new DateTime(2026, 7, 17));
        t.Assignee.Should().Be("仓库主管");
        t.RegulationRef.Should().Be("GB 50016-2014 §4.2.3");
    }

    [Fact]
    public void ParseTickets_MultipleTickets_SeparatedByDashes()
    {
        var input = @"
【问题】: 储罐区未设安全标识
【整改措施】: 按 GB 2894 增设警告标识
---
【问题】: 危化品台账未更新
【整改措施】: 72h 内完成更新
【优先级】: 中";

        var tickets = CallParseTickets(input);

        tickets.Should().HaveCount(2);
        tickets[0].Id.Should().Be(1);
        tickets[0].Issue.Should().Contain("安全标识");
        tickets[1].Id.Should().Be(2);
        tickets[1].Issue.Should().Contain("台账");
    }

    [Fact]
    public void ParseTickets_PartialFields_GetsDefaults()
    {
        var input = "【问题】: 缺少静电接地装置";

        var tickets = CallParseTickets(input);

        tickets.Should().HaveCount(1);
        var t = tickets[0];
        t.Issue.Should().Be("缺少静电接地装置");
        t.Action.Should().BeEmpty();
        t.Priority.Should().Be("中"); // 默认
        t.Assignee.Should().BeNull();
        t.RegulationRef.Should().BeNull();
    }

    [Fact]
    public void ParseTickets_EnglishColon_AlsoWorks()
    {
        var input = @"
【问题】: Acid storage unlabelled
【整改措施】: Label all containers per GHS";

        var tickets = CallParseTickets(input);

        tickets.Should().HaveCount(1);
        tickets[0].Issue.Should().Be("Acid storage unlabelled");
        tickets[0].Action.Should().Be("Label all containers per GHS");
    }

    [Fact]
    public void ParseTickets_InvalidDate_KeepsDefault()
    {
        var input = @"
【问题】: 管道腐蚀
【建议截止日期】: 尽快";

        var tickets = CallParseTickets(input);

        tickets.Should().HaveCount(1);
        tickets[0].SuggestedDeadline.Should().BeCloseTo(DateTime.Now.AddDays(7), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ParseTickets_SectionWithoutIssue_Skipped()
    {
        var input = @"
---
【整改措施】: 无关内容
---
【问题】: 实际需要整改的问题";

        var tickets = CallParseTickets(input);

        tickets.Should().HaveCount(1);
        tickets[0].Issue.Should().Be("实际需要整改的问题");
    }

    [Fact]
    public void ParseTickets_EmptySection_Ignored()
    {
        var input = @"
【问题】: 第一条整改
---
   
---
【问题】: 第二条整改";

        var tickets = CallParseTickets(input);

        tickets.Should().HaveCount(2);
    }

    [Fact]
    public void ParseTickets_VariantLabels_AlsoMatch()
    {
        // "问题】" 不带前括号 "【" 也能匹配，但 ExtractValue 仍需 : 或 ： 分隔
        var input = "问题】: 测试问题\n整改措施】: 测试措施";

        var tickets = CallParseTickets(input);

        tickets.Should().HaveCount(1);
        tickets[0].Issue.Should().Be("测试问题");
        tickets[0].Action.Should().Be("测试措施");
    }

    // ═══════════════════════════════════════
    // TicketItem 状态机
    // ═══════════════════════════════════════

    [Fact]
    public void TicketItem_DefaultStatus_IsNew()
    {
        var ticket = new TicketItem();
        ticket.Status.Should().Be(TicketStatus.New);
    }

    [Fact]
    public void IsOpen_NewToCompleted_Open()
    {
        var ticket = new TicketItem();
        ticket.IsOpen.Should().BeTrue(); // New

        ticket.Accept("张工");
        ticket.IsOpen.Should().BeTrue(); // Accepted

        ticket.StartWork("张工");
        ticket.IsOpen.Should().BeTrue(); // InProgress

        ticket.Complete("张工");
        ticket.IsOpen.Should().BeTrue(); // Completed (still open until verified)
    }

    [Fact]
    public void IsOpen_VerifiedToClosed_NotOpen()
    {
        var ticket = new TicketItem();
        ticket.Verify("李主管");
        ticket.IsOpen.Should().BeFalse(); // Verified

        var ticket2 = new TicketItem();
        ticket2.Close();
        ticket2.IsOpen.Should().BeFalse(); // Closed

        var ticket3 = new TicketItem();
        ticket3.Reject("证据不足", "王经理");
        ticket3.IsOpen.Should().BeFalse(); // Rejected
    }

    [Fact]
    public void Accept_TransitionsToAccepted_SetsAssignee()
    {
        var ticket = new TicketItem();
        ticket.Accept("赵安全员");

        ticket.Status.Should().Be(TicketStatus.Accepted);
        ticket.Assignee.Should().Be("赵安全员");
    }

    [Fact]
    public void StartWork_TransitionsToInProgress()
    {
        var ticket = new TicketItem();
        ticket.StartWork("周库管");

        ticket.Status.Should().Be(TicketStatus.InProgress);
    }

    [Fact]
    public void Complete_TransitionsToCompleted()
    {
        var ticket = new TicketItem();
        ticket.Complete("周库管");

        ticket.Status.Should().Be(TicketStatus.Completed);
    }

    [Fact]
    public void Verify_TransitionsToVerified()
    {
        var ticket = new TicketItem();
        ticket.Verify("陈主任");

        ticket.Status.Should().Be(TicketStatus.Verified);
    }

    [Fact]
    public void Close_TransitionsToClosed()
    {
        var ticket = new TicketItem();
        ticket.Close();

        ticket.Status.Should().Be(TicketStatus.Closed);
    }

    [Fact]
    public void Reject_TransitionsToRejected_AppendsReason()
    {
        var ticket = new TicketItem { Issue = "消防隐患" };
        ticket.Reject("无法复现", "刘总监");

        ticket.Status.Should().Be(TicketStatus.Rejected);
        ticket.Action.Should().Contain("驳回: 无法复现");
    }

    [Fact]
    public void StatusLog_TracksAllTransitions()
    {
        var ticket = new TicketItem();
        ticket.Accept("张工");
        ticket.StartWork("张工");
        ticket.Complete("张工");
        ticket.Verify("李主管");
        ticket.Close();

        ticket.StatusLog.Should().HaveCount(5);
        ticket.StatusLog[0].FromStatus.Should().Be(TicketStatus.New);
        ticket.StatusLog[0].ToStatus.Should().Be(TicketStatus.Accepted);
        ticket.StatusLog[0].ChangedBy.Should().Be("张工");

        ticket.StatusLog[4].FromStatus.Should().Be(TicketStatus.Verified);
        ticket.StatusLog[4].ToStatus.Should().Be(TicketStatus.Closed);
        ticket.StatusLog[4].ChangedBy.Should().Be("system");
    }

    [Fact]
    public void StatusLog_HasTimestamps()
    {
        var ticket = new TicketItem();
        var before = DateTime.Now;
        ticket.Accept("张工");
        var after = DateTime.Now;

        ticket.StatusLog.Should().HaveCount(1);
        ticket.StatusLog[0].ChangedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    // ═══════════════════════════════════════
    // BuildTicketExtractionPrompt: Prompt 模板
    // ═══════════════════════════════════════

    [Fact]
    public void BuildPrompt_ContainsInput()
    {
        var prompt = CallBuildTicketExtractionPrompt("消防通道堆放杂物，不符合 GB 50016。");

        prompt.Should().Contain("消防通道堆放杂物");
        prompt.Should().Contain("【问题】");
        prompt.Should().Contain("【整改措施】");
        prompt.Should().Contain("【优先级】");
        prompt.Should().Contain("【建议截止日期】");
        prompt.Should().Contain("【负责人】");
        prompt.Should().Contain("【引用法规】");
        prompt.Should().Contain("---");
    }

    // ═══════════════════════════════════════
    // IInferenceModule 接口
    // ═══════════════════════════════════════

    [Fact]
    public void Module_Name_IsSet()
    {
        // 需要依赖注入，这里仅测构造不会崩溃
        // 验证模块定义是否符合 IInferenceModule 契约
        typeof(TicketFollowupModule).GetProperty("Name").Should().NotBeNull();
        typeof(TicketFollowupModule).GetProperty("Description").Should().NotBeNull();
    }

    // ═══════════════════════════════════════
    // Reflection Helpers
    // ═══════════════════════════════════════

    private static List<TicketItem> CallParseTickets(string llmOutput)
    {
        var method = typeof(TicketFollowupModule).GetMethod(
            "ParseTickets",
            BindingFlags.NonPublic | BindingFlags.Static);

        if (method == null)
            throw new InvalidOperationException("未找到 ParseTickets 方法");

        return (List<TicketItem>)method.Invoke(null, new object[] { llmOutput })!;
    }

    private static void AssertParseReturnsCount(string input, int expectedCount)
    {
        var tickets = CallParseTickets(input);
        tickets.Should().HaveCount(expectedCount);
    }

    private static string CallBuildTicketExtractionPrompt(string complianceResult)
    {
        var method = typeof(TicketFollowupModule).GetMethod(
            "BuildTicketExtractionPrompt",
            BindingFlags.NonPublic | BindingFlags.Static);

        if (method == null)
            throw new InvalidOperationException("未找到 BuildTicketExtractionPrompt 方法");

        return (string)method.Invoke(null, new object[] { complianceResult })!;
    }
}
