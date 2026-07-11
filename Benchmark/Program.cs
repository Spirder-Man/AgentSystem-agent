using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

class Program
{
    static string BaseUrl = "http://localhost:5000";
    const int WarmupRequests = 3;
    static int MeasureRequests = 20;
    static int Concurrency = 10;
    static bool JsonOutput = false;

    static string? _token = null;
    static string? _refreshToken = null;

    static async Task Main(string[] args)
    {
        // CLI: dotnet run -- [concurrency] [requests] [baseUrl] [--json]
        if (args.Length > 0) int.TryParse(args[0], out Concurrency);
        if (args.Length > 1) int.TryParse(args[1], out MeasureRequests);
        if (args.Length > 2) BaseUrl = args[2];
        JsonOutput = args.Contains("--json");

        Concurrency = Math.Clamp(Concurrency, 1, 100);
        MeasureRequests = Math.Clamp(MeasureRequests, 1, 1000);

        using var http = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(60) };

        Console.WriteLine("========================================");
        Console.WriteLine(" Agent1 API Precision Benchmark (C#)");
        Console.WriteLine("========================================");
        Console.WriteLine($" Concurrency: {Concurrency} | Requests: {MeasureRequests}");
        Console.WriteLine("========================================\n");

        // Get token
        try
        {
            var loginResp = await http.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "admin123" });
            var loginData = await loginResp.Content.ReadFromJsonAsync<JsonElement>();
            _token = loginData.GetProperty("token").GetString()!;
            _refreshToken = loginData.GetProperty("refreshToken").GetString()!;
            Console.WriteLine("[AUTH] Token obtained successfully\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AUTH] FAILED: {ex.Message}\n");
        }

        var results = new List<LatencyStats>();

        results.Add(await RunBenchmark(http, "health/live", "GET", "/health/live"));
        results.Add(await RunBenchmark(http, "health (full)", "GET", "/health"));
        results.Add(await RunBenchmark(http, "metrics", "GET", "/metrics"));
        results.Add(await RunBenchmark(http, "auth/login", "POST", "/api/auth/login", """{"username":"admin","password":"admin123"}"""));

        if (_refreshToken != null)
            results.Add(await RunBenchmark(http, "auth/refresh", "POST", "/api/auth/refresh", $"{{\"refreshToken\":\"{_refreshToken}\"}}"));

        if (_token != null)
        {
            results.Add(await RunBenchmark(http, "compliance/check", "POST", "/api/compliance/check", """{"query":"benzene safety requirements"}""", useAuth: true));
            results.Add(await RunBenchmark(http, "hazard/query", "POST", "/api/compliance/hazard/query", """{"query":"benzene hazard class"}""", useAuth: true));
            results.Add(await RunBenchmark(http, "storage/compat", "POST", "/api/compliance/storage/compatibility", """{"query":"benzene and acetone"}""", useAuth: true));
            results.Add(await RunBenchmark(http, "admin/config/decoupled", "GET", "/admin/config/decoupled-architecture", useAuth: true));
            results.Add(await RunBenchmark(http, "cache/stats", "GET", "/cache/stats", useAuth: true));
            results.Add(await RunBenchmark(http, "memory/stats", "GET", "/memory/stats", useAuth: true));
        }

        // Summary
        Console.WriteLine("\n========================================");
        Console.WriteLine(" SUMMARY");
        Console.WriteLine("========================================");
        Console.WriteLine($"{"Endpoint",-24} {"OK",5} {"FAIL",5} {"QPS",7} {"Avg",8} {"P50",8} {"P95",8} {"P99",8} {"Min",6} {"Max",6}");
        Console.WriteLine(new string('-', 90));

        int totalOk = 0, totalFail = 0;
 foreach (var r in results)
        {
            Console.WriteLine($"{r.Name,-24} {r.Ok,5} {r.Fail,5} {r.Qps,6:F1} {r.Avg,7:F1}ms {r.P50,7:F1}ms {r.P95,7:F1}ms {r.P99,7:F1}ms {r.Min,5:F0}ms {r.Max,5:F0}ms");
            totalOk += r.Ok;
            totalFail += r.Fail;
        }
        Console.WriteLine(new string('-', 90));
        Console.WriteLine($"{"TOTAL",-24} {totalOk,5} {totalFail,5}");
        Console.WriteLine("========================================\n");

        // JSON baseline output (用于 CI 版本化管理)
        if (JsonOutput)
        {
            var baseline = new
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                target = BaseUrl,
                concurrency = Concurrency,
                requests = MeasureRequests,
                results = results.Select(r => new
                {
                    endpoint = r.Name,
                    ok = r.Ok,
                    fail = r.Fail,
                    qps = Math.Round(r.Qps, 1),
                    avg_ms = Math.Round(r.Avg, 1),
                    p50_ms = Math.Round(r.P50, 1),
                    p95_ms = Math.Round(r.P95, 1),
                    p99_ms = Math.Round(r.P99, 1),
                    min_ms = (int)r.Min,
                    max_ms = (int)r.Max
                })
            };
            var json = System.Text.Json.JsonSerializer.Serialize(baseline, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            var baselinePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baseline.json");
            File.WriteAllText(baselinePath, json);
            Console.WriteLine($"[BASELINE] 已写入: {baselinePath}");
        }
    }

    static async Task<LatencyStats> RunBenchmark(HttpClient http, string name, string method, string endpoint, string? body = null, bool useAuth = false)
    {
        Console.Write($"  {name,-22} ");
        var latencies = new ConcurrentBag<double>();

        // Warmup
        for (int i = 0; i < WarmupRequests; i++)
        {
            try
            {
                var req = BuildRequest(method, endpoint, body, useAuth);
                var sw = Stopwatch.StartNew();
                var resp = await http.SendAsync(req);
                sw.Stop();
            }
            catch { /* 压测异常不中断——单次请求失败继续下一轮 */ }
        }

        // Measure
        var tasks = new List<Task>();
        var totalSw = Stopwatch.StartNew();
        int ok = 0, fail = 0;

        for (int i = 0; i < MeasureRequests; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var req = BuildRequest(method, endpoint, body, useAuth);
                    var sw = Stopwatch.StartNew();
                    var resp = await http.SendAsync(req);
                    sw.Stop();
                    latencies.Add(sw.Elapsed.TotalMilliseconds);
                    if (resp.IsSuccessStatusCode)
                        Interlocked.Increment(ref ok);
                    else
                        Interlocked.Increment(ref fail);
                }
                catch
                {
                    Interlocked.Increment(ref fail);
                }
            }));

            while (tasks.Count(t => !t.IsCompleted) >= Concurrency)
                await Task.Delay(10);
        }

        await Task.WhenAll(tasks);
        totalSw.Stop();

        var sorted = latencies.OrderBy(x => x).ToList();
        var avg = sorted.Count > 0 ? sorted.Average() : 0;
        var p50 = sorted.Count > 0 ? Percentile(sorted, 0.50) : 0;
        var p95 = sorted.Count > 0 ? Percentile(sorted, 0.95) : 0;
        var p99 = sorted.Count > 0 ? Percentile(sorted, 0.99) : 0;
        var min = sorted.Count > 0 ? sorted.Min() : 0;
        var max = sorted.Count > 0 ? sorted.Max() : 0;
        var qps = totalSw.Elapsed.TotalSeconds > 0 ? ok / totalSw.Elapsed.TotalSeconds : 0;

        Console.WriteLine($"OK:{ok,3}/{MeasureRequests}  QPS:{qps,6:F1}  avg:{avg,7:F1}ms  p50:{p50,7:F1}ms  p95:{p95,7:F1}ms  p99:{p99,7:F1}ms  min:{min,5:F0}ms  max:{max,5:F0}ms");
        return new LatencyStats(name, ok, fail, qps, avg, p50, p95, p99, min, max);
    }

    static HttpRequestMessage BuildRequest(string method, string endpoint, string? body, bool useAuth)
    {
        var req = new HttpRequestMessage(new HttpMethod(method), endpoint);
        if (body != null)
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (useAuth && _token != null)
            req.Headers.Add("Authorization", $"Bearer {_token}");
        return req;
    }

    static double Percentile(List<double> sorted, double p)
    {
        var idx = (int)Math.Floor(sorted.Count * p);
        return idx < sorted.Count ? sorted[idx] : sorted[^1];
    }

    record LatencyStats(string Name, int Ok, int Fail, double Qps, double Avg, double P50, double P95, double P99, double Min, double Max);
}
