
using Agent1.Services;
using System;
using System.Threading.Tasks;

namespace Agent1.Modules
{
    public class UnifiedDialogModule : IInferenceModule
    {
        private readonly AgentDialog _dialog;
        
        public string Name => "智能对话系统";
        public string Description => "统一的智能对话系统，自动处理简单对话和工业诊断";

        public UnifiedDialogModule(AgentDialog dialog)
        {
            _dialog = dialog;
        }

        public async Task RunAsync()
        {
            Console.WriteLine("══════════════════════════════════════════");
            Console.WriteLine("           智能对话系统");
            Console.WriteLine("══════════════════════════════════════════");
            Console.WriteLine("\n💡 可用命令:");
            Console.WriteLine("  'exit' 或 '退出' - 退出当前模式");
            Console.WriteLine("  'clear' 或 '清空' - 清空对话历史");
            Console.WriteLine("  'history' 或 '历史' - 查看对话历史");
            Console.WriteLine("──────────────────────────────────────────\n");

            var session = _dialog.CreateSession(SessionType.General);
            Console.WriteLine($"✅ 会话已创建: {session.SessionId}\n");

            while (true)
            {
                Console.Write("👤 请输入: ");
                Console.ForegroundColor = ConsoleColor.Green;
                var input = Console.ReadLine() ?? string.Empty;
                Console.ResetColor();

                if (IsExitCommand(input))
                {
                    Console.WriteLine("🚪 退出对话模式");
                    break;
                }

                if (IsClearCommand(input))
                {
                    SessionManager.ClearDialogHistory(session.SessionId);
                    _dialog.ClearMemory();
                    Console.WriteLine("✅ 已清空对话历史和记忆\n");
                    continue;
                }

                if (IsHistoryCommand(input))
                {
                    Console.WriteLine("\n📜 " + _dialog.GetFormattedHistory(session.SessionId));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(input))
                {
                    Console.WriteLine("⚠️ 请输入有效内容\n");
                    continue;
                }

                try
                {
                    await _dialog.ExecuteAsync(input, session);
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n❌ 处理出错: {ex.Message}");
                    Console.ResetColor();
                }

                Console.WriteLine();
            }
        }

        private bool IsExitCommand(string input)
        {
            return input.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                   input.Equals("退出", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsClearCommand(string input)
        {
            return input.Equals("clear", StringComparison.OrdinalIgnoreCase) ||
                   input.Equals("清空", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsHistoryCommand(string input)
        {
            return input.Equals("history", StringComparison.OrdinalIgnoreCase) ||
                   input.Equals("历史", StringComparison.OrdinalIgnoreCase);
        }
    }
}

