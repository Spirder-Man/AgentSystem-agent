using System;
using System.Threading.Tasks;

namespace Agent1.Services
{
    public interface ILlmService
    {
        Task<string> InvokeStreamAsync(string prompt, ConsoleColor color);
        Task<string> InvokeStreamWithRetryAsync(string prompt, ConsoleColor color, string stageName = "");
    }
}