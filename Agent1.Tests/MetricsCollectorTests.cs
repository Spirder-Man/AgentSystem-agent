using System;
using System.Threading.Tasks;
using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

public class MetricsCollectorTests
{
    [Fact]
    public void RecordLlmCall_Success_IncrementsCounterAndTracksDuration()
    {
        var before = MetricsCollector.GetSnapshot();
        MetricsCollector.RecordLlmCall(1000, true, 0);
        var after = MetricsCollector.GetSnapshot();

        after.LlmCallCount.Should().Be(before.LlmCallCount + 1);
        after.LlmErrorCount.Should().Be(before.LlmErrorCount);
    }

    [Fact]
    public void RecordLlmCall_Failure_IncrementsErrorCount()
    {
        var before = MetricsCollector.GetSnapshot();
        MetricsCollector.RecordLlmCall(500, false, 2);
        var after = MetricsCollector.GetSnapshot();

        after.LlmCallCount.Should().Be(before.LlmCallCount + 1);
        after.LlmErrorCount.Should().Be(before.LlmErrorCount + 1);
        after.LlmRetryCount.Should().Be(before.LlmRetryCount + 2);
    }

    [Fact]
    public void RecordLlmCall_MultipleCalls_CorrectAverage()
    {
        // Record two calls: 1000ms and 3000ms → average should be 2000ms
        MetricsCollector.RecordLlmCall(1000, true, 0);
        MetricsCollector.RecordLlmCall(3000, true, 1);

        var snap = MetricsCollector.GetSnapshot();
        snap.LlmAvgDurationMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RecordRagSearch_TracksDurationAndCacheHits()
    {
        var before = MetricsCollector.GetSnapshot();
        MetricsCollector.RecordRagSearch(150, cacheHit: true);
        MetricsCollector.RecordRagSearch(200, cacheHit: false);
        var after = MetricsCollector.GetSnapshot();

        after.RagSearchCount.Should().Be(before.RagSearchCount + 2);
        after.RagCacheHitCount.Should().Be(before.RagCacheHitCount + 1);
    }

    [Fact]
    public void RecordApiRequest_TracksSuccessAndFailure()
    {
        var before = MetricsCollector.GetSnapshot();
        MetricsCollector.RecordApiRequest(true);
        MetricsCollector.RecordApiRequest(false);
        MetricsCollector.RecordApiRequest(true);
        var after = MetricsCollector.GetSnapshot();

        after.ApiRequestCount.Should().Be(before.ApiRequestCount + 3);
        after.ApiErrorCount.Should().Be(before.ApiErrorCount + 1);
    }

    [Fact]
    public void GetSnapshot_ReturnsConsistentData()
    {
        var snap = MetricsCollector.GetSnapshot();
        snap.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        snap.LlmCallCount.Should().BeGreaterOrEqualTo(0);
        snap.RagSearchCount.Should().BeGreaterOrEqualTo(0);
        snap.ApiRequestCount.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public void ToPrometheusFormat_ContainsExpectedMetrics()
    {
        // Ensure some data exists
        MetricsCollector.RecordLlmCall(500, true, 0);
        MetricsCollector.RecordRagSearch(100, false);
        MetricsCollector.RecordApiRequest(true);

        var prom = MetricsCollector.ToPrometheusFormat();

        prom.Should().Contain("agent1_llm_calls_total");
        prom.Should().Contain("agent1_llm_duration_ms_avg");
        prom.Should().Contain("agent1_llm_errors_total");
        prom.Should().Contain("agent1_rag_searches_total");
        prom.Should().Contain("agent1_rag_duration_ms_avg");
        prom.Should().Contain("agent1_rag_cache_hits_total");
        prom.Should().Contain("agent1_api_requests_total");
        prom.Should().Contain("agent1_api_errors_total");
    }

    [Fact]
    public void ToPrometheusFormat_ValidFormat()
    {
        var prom = MetricsCollector.ToPrometheusFormat();
        prom.Should().NotBeNullOrWhiteSpace();

        // Every metric line should have HELP and TYPE
        prom.Should().Contain("# HELP");
        prom.Should().Contain("# TYPE");

        // Lines should not end with trailing whitespace
        foreach (var line in prom.Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(line))
                line.Should().NotEndWith(" ");
        }
    }

    [Fact]
    public async Task Snapshot_IsThreadSafe_BasicTest()
    {
        // Concurrently record from multiple tasks
        var tasks = new Task[10];
        for (int i = 0; i < 10; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                {
                    MetricsCollector.RecordLlmCall(j % 100, j % 3 != 0, j % 2);
                    MetricsCollector.RecordRagSearch(j % 50, j % 5 == 0);
                    MetricsCollector.RecordApiRequest(true);
                }
            });
        }
        await Task.WhenAll(tasks);

        // Snapshot should not throw
        var snap = MetricsCollector.GetSnapshot();
        snap.LlmCallCount.Should().BeGreaterOrEqualTo(1000);

        // Prometheus format should also work
        var prom = MetricsCollector.ToPrometheusFormat();
        prom.Should().NotBeNullOrWhiteSpace();
    }
}
