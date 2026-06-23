using System.Text;
using Agent1.Services;
using Agent1.Models;

namespace Agent1.Modules
{
    /// <summary>
    /// [P1] 整改工单跟进模块 — 从合规检查结果中提取整改项，生成结构化工单。
    /// 支持 控制台交互 和 API 调用两种模式。
    /// </summary>
    public class TicketFollowupModule : IInferenceModule
    {
        public string Name => "整改工单跟进";
        public string Description => "输入合规检查结果或巡检记录，自动提取整改项并生成工单";

        public Task<CliExecutionResult> RunWithResultAsync(string userInput)
            => Task.FromResult(new CliExecutionResult
            {
                Success = true,
                DisplayOutput = "整改工单跟进模块仅支持交互式运行，请使用 RunAsync() 或 API 调用 ProcessFollowupAsync()",
                AuditRecord = "交互模式"
            });

        private readonly ILlmService _llmService;
        private readonly IKnowledgeBaseService _kbService;
        private readonly IAuditService _auditService;

        public TicketFollowupModule(
            ILlmService llmService,
            IKnowledgeBaseService kbService,
            IAuditService auditService)
        {
            _llmService = llmService;
            _kbService = kbService;
            _auditService = auditService;
        }

        public async Task RunAsync()
        {
            Console.WriteLine("\n========== 整改工单跟进 ==========");
            Console.WriteLine("请输入合规检查结果或巡检记录:");
            Console.Write("> ");
            var input = Console.ReadLine() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine("输入不能为空！");
                return;
            }

            await ProcessFollowupAsync(input);
        }

        /// <summary>
        /// 核心处理逻辑：LLM 提取整改项 + 审计记录。也供 API 直接调用。
        /// </summary>
        public async Task<List<TicketItem>> ProcessFollowupAsync(string complianceResult)
        {
            // 1. 使用 LLM 分析合规问题并提取整改项
            var prompt = BuildTicketExtractionPrompt(complianceResult);
            var llmResponse = await _llmService.InvokeStreamAsync(prompt, ConsoleColor.Cyan);

            // 2. 解析 LLM 输出为结构化工单
            var tickets = ParseTickets(llmResponse);

            // 3. 输出工单
            Console.WriteLine("\n========== 生成的整改工单 ==========");
            foreach (var ticket in tickets)
            {
                Console.WriteLine($"\n📋 工单 #{ticket.Id}");
                Console.WriteLine($"   问题: {ticket.Issue}");
                Console.WriteLine($"   整改措施: {ticket.Action}");
                Console.WriteLine($"   优先级: {ticket.Priority}");
                Console.WriteLine($"   建议截止日期: {ticket.SuggestedDeadline:yyyy-MM-dd}");
                Console.WriteLine($"   负责人: {ticket.Assignee ?? "待分配"}");
                Console.WriteLine($"   引用法规: {ticket.RegulationRef ?? "未指定"}");
            }
            Console.WriteLine($"\n共生成 {tickets.Count} 个整改工单");

            // 4. 审计记录
            await _auditService.LogOperationAsync("system", "TicketFollowup",
                $"从合规结果中提取 {tickets.Count} 个整改项");

            return tickets;
        }

        private static string BuildTicketExtractionPrompt(string complianceResult)
        {
            var sb = new StringBuilder();
            sb.AppendLine("你是化工园区安全管理员。请从以下合规检查结果中提取所有需要整改的问题项。");
            sb.AppendLine();
            sb.AppendLine("对每个问题，按以下格式输出（每项以 --- 分隔）：");
            sb.AppendLine("---");
            sb.AppendLine("【问题】: <具体问题描述>");
            sb.AppendLine("【整改措施】: <建议的整改行动>");
            sb.AppendLine("【优先级】: <高/中/低>");
            sb.AppendLine("【建议截止日期】: <YYYY-MM-DD 格式，基于紧急程度>");
            sb.AppendLine("【负责人】: <建议角色，如安全员/仓库主管/消防负责人>");
            sb.AppendLine("【引用法规】: <相关 GB 标准编号，如有>");
            sb.AppendLine();
            sb.AppendLine("合规检查结果:");
            sb.AppendLine(complianceResult);
            sb.AppendLine();
            sb.AppendLine("请提取所有整改项（如果没有需要整改的，返回\"无需整改\"）：");

            return sb.ToString();
        }

        private static List<TicketItem> ParseTickets(string llmOutput)
        {
            var tickets = new List<TicketItem>();

            if (string.IsNullOrWhiteSpace(llmOutput) || llmOutput.Contains("无需整改"))
                return tickets;

            // 按 --- 分隔每个工单项
            var sections = llmOutput.Split("---", StringSplitOptions.RemoveEmptyEntries);
            int ticketId = 1;

            foreach (var section in sections)
            {
                var trimmed = section.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                var ticket = new TicketItem { Id = ticketId++ };

                foreach (var line in trimmed.Split('\n'))
                {
                    if (line.Contains("【问题】") || line.Contains("问题】"))
                        ticket.Issue = ExtractValue(line);
                    else if (line.Contains("【整改措施】") || line.Contains("整改措施】"))
                        ticket.Action = ExtractValue(line);
                    else if (line.Contains("【优先级】") || line.Contains("优先级】"))
                        ticket.Priority = ExtractValue(line);
                    else if (line.Contains("【建议截止日期】") || line.Contains("截止日期】"))
                        ParseDeadline(ticket, line);
                    else if (line.Contains("【负责人】") || line.Contains("负责人】"))
                        ticket.Assignee = ExtractValue(line);
                    else if (line.Contains("【引用法规】") || line.Contains("法规】"))
                        ticket.RegulationRef = ExtractValue(line);
                }

                // 至少有问题的工单才加入
                if (!string.IsNullOrWhiteSpace(ticket.Issue))
                    tickets.Add(ticket);
            }

            return tickets;
        }

        private static string ExtractValue(string line)
        {
            var idx = line.IndexOf('：');
            if (idx < 0) idx = line.IndexOf(':');
            if (idx >= 0 && idx < line.Length - 1)
                return line[(idx + 1)..].Trim();
            return string.Empty;
        }

        private static void ParseDeadline(TicketItem ticket, string line)
        {
            var value = ExtractValue(line);
            if (DateTime.TryParse(value, out var dt))
                ticket.SuggestedDeadline = dt;
        }
    }

    /// <summary>
    /// 整改工单项模型 — 范式 2 状态机版本。
    /// 
    /// 对标 Dependency-Track 的 Finding 状态机:
    ///   DT: NEW → IN_REVIEW → REMEDIATED → FALSE_POSITIVE
    ///   化工: New → Accepted → InProgress → Completed → Verified → Closed | Rejected
    /// </summary>
    public class TicketItem
    {
        public int Id { get; set; }
        public string Issue { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Priority { get; set; } = "中";
        public DateTime SuggestedDeadline { get; set; } = DateTime.Now.AddDays(7);
        public string? Assignee { get; set; }
        public string? RegulationRef { get; set; }

        /// <summary>工单状态（范式 2 状态机）</summary>
        public TicketStatus Status { get; set; } = TicketStatus.New;

        /// <summary>状态变更日志</summary>
        public List<TicketStatusLog> StatusLog { get; set; } = new();

        /// <summary>是否未关闭（仍需要关注）</summary>
        public bool IsOpen => Status != TicketStatus.Closed &&
                              Status != TicketStatus.Verified &&
                              Status != TicketStatus.Rejected;

        // ── 状态流转方法 ──

        public void Accept(string assignee)
        {
            TransitionTo(TicketStatus.Accepted, assignee);
            Assignee = assignee;
        }

        public void StartWork(string operator_)
        {
            TransitionTo(TicketStatus.InProgress, operator_);
        }

        public void Complete(string operator_)
        {
            TransitionTo(TicketStatus.Completed, operator_);
        }

        public void Verify(string verifier)
        {
            TransitionTo(TicketStatus.Verified, verifier);
        }

        public void Close()
        {
            TransitionTo(TicketStatus.Closed, "system");
        }

        public void Reject(string reason, string operator_)
        {
            TransitionTo(TicketStatus.Rejected, operator_);
            Action += $" [驳回: {reason}]";
        }

        private void TransitionTo(TicketStatus newStatus, string operator_)
        {
            StatusLog.Add(new TicketStatusLog
            {
                FromStatus = Status,
                ToStatus = newStatus,
                ChangedAt = DateTime.Now,
                ChangedBy = operator_
            });
            Status = newStatus;
        }
    }

    /// <summary>工单状态</summary>
    public enum TicketStatus
    {
        New,         // 新建
        Accepted,    // 已受理（指派了责任人）
        InProgress,  // 整改中
        Completed,   // 已整改（待验收）
        Verified,    // 已验证通过
        Closed,      // 已归档
        Rejected     // 驳回
    }

    /// <summary>工单状态变更日志</summary>
    public class TicketStatusLog
    {
        public TicketStatus FromStatus { get; set; }
        public TicketStatus ToStatus { get; set; }
        public DateTime ChangedAt { get; set; }
        public string ChangedBy { get; set; } = "";
    }
}
