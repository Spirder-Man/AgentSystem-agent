using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Agent1.Models;
using Agent1.Services;
using Agent1.Services.Orchestration;
using Agent1.Services.Monitoring;

namespace Agent1.Commands
{
    /// <summary>
    /// 系统运维菜单 — 收敛所有原20个原子菜单为二级子菜单。
    /// 对话工作台(1)/巡检工作台(2)/应急响应台(3)/合规总览(4) 已独立为一级入口。
    /// </summary>
    public class AdminMenuCommand : IMenuCommand
    {
        private readonly Dictionary<string, IMenuCommand> _subCommands = new();

        public string Key => "5";
        public string Label => "⚙️ 系统运维 [数据库·知识库·诊断·告警·经典菜单]";

        public AdminMenuCommand(
            IModuleFactory moduleFactory, IDatabaseService db, ChemicalRAG chemicalRAG,
            AgentDialog agentDialog, ILlmService llmService, IKnowledgeBaseService kb,
            AlertDispatcher alertDispatcher, ModuleDispatcher dispatcher)
        {
            _subCommands["1"] = new ModuleCommand("1", "合规自查(经典)", ModuleType.ComplianceCheck, dispatcher);
            _subCommands["2"] = new DatabaseValidationCommand(db);
            _subCommands["3"] = new SwitchSearchModeCommand();
            _subCommands["4"] = new FunctionCallingDiagnosticsCommand(agentDialog);
            _subCommands["5"] = new ComplianceEvalCommand(agentDialog, llmService, kb);
            _subCommands["6"] = new TicketFollowupCommand(moduleFactory);
            _subCommands["7"] = new IncrementalKnowledgeBaseCommand(chemicalRAG);
            _subCommands["8"] = new RegulatoryAuditCommand(moduleFactory);
            _subCommands["9"] = new KnowledgeGraphCommand(dispatcher);
            _subCommands["10"] = new TestAlertCommand(alertDispatcher);
            // 推理模式子菜单
            _subCommands["c1"] = new ModuleCommand("c1", "CoT推理(标准)", ModuleType.CoTSolid, dispatcher);
            _subCommands["c2"] = new ModuleCommand("c2", "CoT推理(流式)", ModuleType.CoTStream, dispatcher);
            _subCommands["c3"] = new ModuleCommand("c3", "ReAct推理(标准)", ModuleType.ReActSolid, dispatcher);
            _subCommands["c4"] = new ModuleCommand("c4", "ReAct推理(流式)", ModuleType.ReActStream, dispatcher);
            _subCommands["c5"] = new ModuleCommand("c5", "Reflection反思", ModuleType.Reflection, dispatcher);
            _subCommands["c6"] = new ModuleCommand("c6", "RAG检索增强", ModuleType.RAG, dispatcher);
            _subCommands["c7"] = new ChemicalRagTestCommand(chemicalRAG);
        }

        public async Task ExecuteAsync()
        {
            while (true)
            {
                Console.WriteLine("\n══════════ 系统运维 ══════════");
                Console.WriteLine("── 化工业务 ──");
                Console.WriteLine("  1. 合规自查(经典)    2. 数据库验证      3. 切换检索模式");
                Console.WriteLine("  4. 工具调用诊断      5. 合规评测集      6. 整改工单跟进");
                Console.WriteLine("  7. 知识库增量更新    8. 监管核查辅助    9. 知识图谱");
                Console.WriteLine(" 10. 测试告警邮件");
                Console.WriteLine("── 推理模式 ──");
                Console.WriteLine(" c1.CoT标准  c2.CoT流式  c3.ReAct标准  c4.ReAct流式");
                Console.WriteLine(" c5.Reflection  c6.RAG  c7.RAG测试");
                Console.WriteLine("  0. 返回主菜单");
                Console.Write("\n请选择: ");

                var choice = Console.ReadLine() ?? "0";
                if (choice == "0") return;

                if (_subCommands.TryGetValue(choice, out var cmd))
                {
                    try { await cmd.ExecuteAsync(); }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"\n❌ 执行出错: {ex.Message}");
                        Console.ResetColor();
                    }
                }
                else
                {
                    Console.WriteLine("\n⚠️ 无效选项");
                }
            }
        }
    }
}
