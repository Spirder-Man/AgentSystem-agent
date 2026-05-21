using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Agent1
{
    public class IndustrialTools
    {
        private readonly HttpClient _httpClient;

        public IndustrialTools()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        [KernelFunction, Description("读取机床主轴的实时温度")]
        public string GetSpindleTemperature()
        {
            Random random = new Random();
            int temp = random.Next(190, 220);
            return $"主轴实时温度：{temp}℃";
        }

        [KernelFunction, Description("查询机床主轴温度的安全阈值")]
        public string GetTemperatureThreshold()
        {
            return "主轴安全温度阈值：≤ 180℃，超过即为异常故障";
        }

        [KernelFunction, Description("通过联网搜索获取最新信息，适用于需要实时数据或最新知识的场景")]
        public async Task<string> WebSearch(string query)
        {
            try
            {
                string encodedQuery = Uri.EscapeDataString(query);
                string apiUrl = $"https://api.search.bing.com/v7.0/search?q={encodedQuery}&count=5&mkt=zh-CN";
                
                _httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", "YOUR_BING_API_KEY");
                
                var response = await _httpClient.GetAsync(apiUrl);
                response.EnsureSuccessStatusCode();
                
                string jsonContent = await response.Content.ReadAsStringAsync();
                var jsonDoc = JsonDocument.Parse(jsonContent);
                
                var results = new StringBuilder();
                int count = 0;
                
                foreach (var item in jsonDoc.RootElement.GetProperty("webPages").GetProperty("value").EnumerateArray())
                {
                    if (count >= 3) break;
                    string title = item.GetProperty("name").GetString() ?? string.Empty;
                    string snippet = item.GetProperty("snippet").GetString() ?? string.Empty;
                    string url = item.GetProperty("url").GetString() ?? string.Empty;
                    
                    results.AppendLine($"【{count + 1}】{title}");
                    results.AppendLine($"摘要：{snippet}");
                    results.AppendLine($"来源：{url}\n");
                    count++;
                }
                
                return results.ToString();
            }
            catch (Exception ex)
            {
                return $"搜索失败：{ex.Message}";
            }
        }

        [KernelFunction, Description("获取当前时间和日期")]
        public string GetCurrentTime()
        {
            return $"当前时间：{DateTime.Now.ToString("yyyy年MM月dd日 HH:mm:ss")}";
        }

        [KernelFunction, Description("计算数学表达式，支持加减乘除和括号")]
        public string Calculate(string expression)
        {
            try
            {
                var result = new System.Data.DataTable().Compute(expression, null);
                return $"计算结果：{expression} = {result}";
            }
            catch (Exception ex)
            {
                return $"计算失败：{ex.Message}";
            }
        }
    }
}
