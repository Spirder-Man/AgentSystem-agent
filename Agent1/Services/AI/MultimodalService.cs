using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Agent1.Config;

namespace Agent1.Services
{
    /// <summary>
    /// [P2] 多模态视觉分析服务 — 绕过 Semantic Kernel 高层 API 限制，
    /// 直接通过 HttpClient 调用 Ollama 原生 /api/chat 端点，支持图片输入。
    /// 
    /// 支持的模型：qwen-vl (Qwen2-VL)、llava 等 Ollama 支持的视觉模型。
    /// 适用场景：GHS 标签识别、储罐/管道照片分析、消防设施合规检查。
    /// </summary>
    public class MultimodalService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _modelId;
        private readonly Uri _endpoint;

        public MultimodalService()
        {
            _modelId = ModelConfig.MultimodalModelId;
            _endpoint = ModelConfig.Endpoint;

            _httpClient = new HttpClient(new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer = 2,  // 视觉模型推理慢，限制并发
                EnableMultipleHttp2Connections = true
            })
            {
                Timeout = TimeSpan.FromMinutes(3),
                BaseAddress = _endpoint
            };
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        /// <summary>
        /// 核心方法：传入图片路径和分析提示词，返回 Ollama 视觉模型的文本分析结果。
        /// 图片自动转为 Base64 编码，通过 Ollama 原生 /api/chat 的 images 字段发送。
        /// </summary>
        /// <param name="imagePath">本地图片文件路径（支持 jpg/png/webp）</param>
        /// <param name="prompt">分析提示词（中文即可）</param>
        /// <returns>模型分析文本</returns>
        public async Task<string> AnalyzeImageAsync(string imagePath, string prompt)
        {
            if (!File.Exists(imagePath))
                return $"错误: 图片文件不存在 — {imagePath}";

            try
            {
                // 读取图片并编码为 Base64
                var imageBytes = await File.ReadAllBytesAsync(imagePath);
                var imageBase64 = Convert.ToBase64String(imageBytes);

                // 构建 Ollama 原生 /api/chat 请求体（带 images 字段）
                var request = new
                {
                    model = _modelId,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = prompt,
                            images = new[] { imageBase64 }
                        }
                    },
                    stream = false,
                    options = new { temperature = 0.1 }  // 低温度提高准确性
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("/api/chat", content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    return $"视觉分析请求失败 [{response.StatusCode}]: {Truncate(errorBody)}";
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);
                var result = doc.RootElement.GetProperty("message").GetProperty("content").GetString();
                return result ?? "(模型返回空内容)";
            }
            catch (TaskCanceledException)
            {
                return "视觉分析超时（3分钟），图片可能过大或模型未就绪";
            }
            catch (Exception ex)
            {
                return $"视觉分析异常: {ex.Message}";
            }
        }

        /// <summary>
        /// GHS 标签识别：分析化学品包装上的 GHS 危险标签图片。
        /// 自动提取：危险类别、信号词、危险声明代码（H 语句）、防范声明代码（P 语句）。
        /// </summary>
        public async Task<string> AnalyzeHazardLabelAsync(string imagePath)
        {
            var prompt = @"你是化工安全专家。请分析这张 GHS 化学品标签图片，提取以下信息：
1. 危险象形图（如火焰、骷髅、腐蚀等图标）
2. 信号词（危险/警告）
3. 危险声明 H 代码（如 H225 高度易燃液体）
4. 防范声明 P 代码（如 P210 远离热源）
5. 如果有 UN 编号或 CAS 号，请列出

请用中文输出，格式清晰。如果图片不清晰或无法识别，请明确说明。";

            return await AnalyzeImageAsync(imagePath, prompt);
        }

        /// <summary>
        /// 储罐/管道场景分析：分析化工储罐或管道照片的合规性。
        /// 检查：标识标签完整性、腐蚀/泄漏痕迹、安全附件状态。
        /// </summary>
        public async Task<string> AnalyzeStorageSceneAsync(string imagePath)
        {
            var prompt = @"你是化工园区安全巡检专家。请分析这张储罐/管道照片，检查以下方面：
1. 设备标识标签是否完整、可读（名称、编号、危险性标识）
2. 可见区域是否有腐蚀、锈蚀、泄漏痕迹
3. 安全附件状态（压力表、温度计、安全阀是否正常范围内）
4. 管道色标是否符合 GB 7231 工业管道颜色标识标准
5. 周边环境是否存在安全隐患（杂物堆积、消防通道占用等）

请逐项说明检查结果，指出不合规项。如果信息不足，请明确说明需要补充的信息。";

            return await AnalyzeImageAsync(imagePath, prompt);
        }

        private static string Truncate(string text, int maxLen = 200)
            => text.Length <= maxLen ? text : text[..maxLen] + "...";
    }
}
