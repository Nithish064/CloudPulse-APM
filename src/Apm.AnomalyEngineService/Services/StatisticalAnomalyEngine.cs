using Apm.Shared.Models;
using Apm.Shared.Storage;
using Microsoft.Extensions.Logging;

namespace Apm.AnomalyEngineService.Services;

public class StatisticalAnomalyEngine
{
    private readonly ITelemetryStore _store;
    private readonly ILogger<StatisticalAnomalyEngine> _logger;

    public StatisticalAnomalyEngine(ITelemetryStore store, ILogger<StatisticalAnomalyEngine> logger)
    {
        _store = store;
        _logger = logger;
    }

    public List<AnomalyAlert> EvaluateAnomalies()
    {
        var detectedAlerts = new List<AnomalyAlert>();

        try
        {
            var summaries = _store.GetServiceHealthSummaries();

            foreach (var summary in summaries)
            {
                try
                {
                    // 1. Evaluate Latency Spikes via Z-Score
                    var spans = _store.GetRecentSpans(150, serviceFilter: summary.ServiceName);
                    if (spans.Count >= 5)
                    {
                        var latencies = spans.Select(s => s.DurationMs).ToList();
                        double mean = latencies.Average();
                        double sumOfSquares = latencies.Sum(d => Math.Pow(d - mean, 2));
                        double stdDev = Math.Sqrt(sumOfSquares / latencies.Count);

                        // If stdDev is significant and P95 latency is high
                        if (stdDev > 5.0 && summary.P95LatencyMs > 250.0)
                        {
                            double zScore = (summary.P95LatencyMs - mean) / stdDev;

                            if (zScore >= 2.0)
                            {
                                var severity = zScore switch
                                {
                                    >= 4.0 => AlertSeverity.Critical,
                                    >= 3.0 => AlertSeverity.High,
                                    _ => AlertSeverity.Warning
                                };

                                var alert = new AnomalyAlert
                                {
                                    Title = $"Latency Degraded on {summary.ServiceName}",
                                    ServiceName = summary.ServiceName,
                                    MetricName = "p95_latency_ms",
                                    Severity = severity,
                                    CurrentValue = summary.P95LatencyMs,
                                    BaselineValue = Math.Round(mean, 2),
                                    ZScore = Math.Round(zScore, 2),
                                    Description = $"P95 latency spiked to {summary.P95LatencyMs}ms (Baseline: {mean:0.0}ms, Z-Score: +{zScore:0.2}\u03c3).",
                                    SuggestedRemediation = GenerateLatencyRemediation(summary.ServiceName, summary.P95LatencyMs)
                                };

                                detectedAlerts.Add(alert);
                                _store.AddAlert(alert);

                                _logger.LogWarning(
                                    "[ANOMALY DETECTED] {ServiceName} latency anomaly! ZScore: {ZScore:0.2}, P95: {P95}ms, Baseline: {Baseline}ms",
                                    summary.ServiceName, zScore, summary.P95LatencyMs, mean);
                            }
                        }
                    }

                    // 2. Evaluate Error Rate Surge
                    if (summary.ErrorRatePercent >= 5.0)
                    {
                        var severity = summary.ErrorRatePercent >= 15.0
                            ? AlertSeverity.Critical
                            : AlertSeverity.High;

                        var alert = new AnomalyAlert
                        {
                            Title = $"High Error Rate on {summary.ServiceName}",
                            ServiceName = summary.ServiceName,
                            MetricName = "error_rate_percent",
                            Severity = severity,
                            CurrentValue = summary.ErrorRatePercent,
                            BaselineValue = 0.5,
                            ZScore = Math.Round((summary.ErrorRatePercent - 0.5) / 1.5, 2),
                            Description = $"Error rate surged to {summary.ErrorRatePercent}% of total traffic.",
                            SuggestedRemediation = $"Inspect {summary.ServiceName} logs for unhandled 5xx exceptions or downstream timeout cascades."
                        };

                        detectedAlerts.Add(alert);
                        _store.AddAlert(alert);

                        _logger.LogError(
                            "[ANOMALY DETECTED] {ServiceName} error surge! ErrorRate: {ErrorRate}% (Total Requests: {Total})",
                            summary.ServiceName, summary.ErrorRatePercent, summary.TotalRequests);
                    }
                }
                catch (Exception serviceEvalEx)
                {
                    _logger.LogError(serviceEvalEx, "Error analyzing service metrics for {ServiceName}: {Message}", summary.ServiceName, serviceEvalEx.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error executing EvaluateAnomalies: {Message}", ex.Message);
        }

        return detectedAlerts;
    }

    private static string GenerateLatencyRemediation(string serviceName, double p95Ms)
    {
        return serviceName.ToLowerInvariant() switch
        {
            "order-service" => "Check database locks on Orders table and review Redis cache hit-ratio.",
            "payment-gateway" => "Investigate third-party payment provider endpoint response times; scale circuit breaker thresholds.",
            "inventory-service" => "High serialization overhead detected. Optimize EF Core queries and introduce distributed caching.",
            "auth-service" => "Verify asymmetric JWT signing key cache and Redis session store latency.",
            _ => $"Inspect thread-pool starvation and CPU throttling on container instance for {serviceName}."
        };
    }
}
