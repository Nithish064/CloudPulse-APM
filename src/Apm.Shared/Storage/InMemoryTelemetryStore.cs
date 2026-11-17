using System.Collections.Concurrent;
using Apm.Shared.Models;

namespace Apm.Shared.Storage;

public class InMemoryTelemetryStore : ITelemetryStore
{
    private const int MaxItemsPerQueue = 5000;

    private readonly ConcurrentDictionary<string, List<TelemetrySpan>> _tracesById = new();
    private readonly ConcurrentQueue<TelemetrySpan> _recentSpans = new();
    private readonly ConcurrentQueue<MetricDataPoint> _metrics = new();
    private readonly ConcurrentQueue<LogRecord> _logs = new();
    private readonly ConcurrentDictionary<string, AnomalyAlert> _alerts = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _serviceHeartbeats = new();

    public void AddSpan(TelemetrySpan span)
    {
        _tracesById.AddOrUpdate(
            span.TraceId,
            _ => [span],
            (_, list) =>
            {
                lock (list)
                {
                    list.Add(span);
                }
                return list;
            });

        _recentSpans.Enqueue(span);
        _serviceHeartbeats[span.ServiceName] = DateTimeOffset.UtcNow;

        // Automatically maintain queue size limit
        while (_recentSpans.Count > MaxItemsPerQueue && _recentSpans.TryDequeue(out _)) { }
    }

    public void AddSpans(IEnumerable<TelemetrySpan> spans)
    {
        foreach (var span in spans)
        {
            AddSpan(span);
        }
    }

    public void AddMetric(MetricDataPoint metric)
    {
        _metrics.Enqueue(metric);
        _serviceHeartbeats[metric.ServiceName] = DateTimeOffset.UtcNow;

        while (_metrics.Count > MaxItemsPerQueue && _metrics.TryDequeue(out _)) { }
    }

    public void AddMetrics(IEnumerable<MetricDataPoint> metrics)
    {
        foreach (var metric in metrics)
        {
            AddMetric(metric);
        }
    }

    public void AddLog(LogRecord log)
    {
        _logs.Enqueue(log);
        _serviceHeartbeats[log.ServiceName] = DateTimeOffset.UtcNow;

        while (_logs.Count > MaxItemsPerQueue && _logs.TryDequeue(out _)) { }
    }

    public void AddAlert(AnomalyAlert alert)
    {
        _alerts[alert.Id] = alert;
    }

    public IReadOnlyList<TelemetrySpan> GetRecentSpans(int limit = 100, string? serviceFilter = null, bool? errorOnly = null)
    {
        var query = _recentSpans.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(serviceFilter))
        {
            query = query.Where(s => s.ServiceName.Equals(serviceFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (errorOnly == true)
        {
            query = query.Where(s => s.HasError);
        }

        return query.OrderByDescending(s => s.StartTime).Take(limit).ToList();
    }

    public IReadOnlyList<TelemetrySpan>? GetTraceSpans(string traceId)
    {
        if (_tracesById.TryGetValue(traceId, out var list))
        {
            lock (list)
            {
                return list.OrderBy(s => s.StartTime).ToList();
            }
        }
        return null;
    }

    public IReadOnlyList<MetricDataPoint> GetServiceMetrics(string serviceName, string? metricName = null, int limit = 60)
    {
        var query = _metrics.Where(m => m.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(metricName))
        {
            query = query.Where(m => m.MetricName.Equals(metricName, StringComparison.OrdinalIgnoreCase));
        }

        return query.OrderByDescending(m => m.Timestamp).Take(limit).OrderBy(m => m.Timestamp).ToList();
    }

    public IReadOnlyList<LogRecord> GetRecentLogs(int limit = 100, string? serviceFilter = null, string? levelFilter = null)
    {
        var query = _logs.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(serviceFilter))
        {
            query = query.Where(l => l.ServiceName.Equals(serviceFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(levelFilter))
        {
            query = query.Where(l => l.Level.Equals(levelFilter, StringComparison.OrdinalIgnoreCase));
        }

        return query.OrderByDescending(l => l.Timestamp).Take(limit).ToList();
    }

    public IReadOnlyList<AnomalyAlert> GetAlerts(int limit = 50, bool unacknowledgedOnly = false)
    {
        var query = _alerts.Values.AsEnumerable();

        if (unacknowledgedOnly)
        {
            query = query.Where(a => !a.IsAcknowledged);
        }

        return query.OrderByDescending(a => a.DetectedAt).Take(limit).ToList();
    }

    public bool AcknowledgeAlert(string alertId)
    {
        if (_alerts.TryGetValue(alertId, out var alert))
        {
            alert.IsAcknowledged = true;
            return true;
        }
        return false;
    }

    public IReadOnlyList<ServiceHealthSummary> GetServiceHealthSummaries()
    {
        var services = _recentSpans.Select(s => s.ServiceName).Distinct().ToList();

        // Default known services if store is brand new
        if (services.Count == 0)
        {
            services = ["auth-service", "order-service", "payment-gateway", "inventory-service"];
        }

        var summaries = new List<ServiceHealthSummary>();

        foreach (var svc in services)
        {
            var serviceSpans = _recentSpans.Where(s => s.ServiceName.Equals(svc, StringComparison.OrdinalIgnoreCase)).ToList();
            var serviceAlerts = _alerts.Values.Count(a => a.ServiceName.Equals(svc, StringComparison.OrdinalIgnoreCase) && !a.IsAcknowledged);

            if (serviceSpans.Count == 0)
            {
                summaries.Add(new ServiceHealthSummary
                {
                    ServiceName = svc,
                    Status = "Healthy",
                    AvgLatencyMs = 45.0,
                    P95LatencyMs = 95.0,
                    P99LatencyMs = 120.0,
                    ErrorRatePercent = 0.0,
                    TotalRequests = 0,
                    ActiveAlertsCount = serviceAlerts,
                    LastHeartbeat = _serviceHeartbeats.GetValueOrDefault(svc, DateTimeOffset.UtcNow)
                });
                continue;
            }

            var latencies = serviceSpans.Select(s => s.DurationMs).OrderBy(d => d).ToList();
            double avg = latencies.Average();
            double p95 = latencies[(int)(latencies.Count * 0.95)];
            double p99 = latencies[(int)(latencies.Count * 0.99)];
            int errors = serviceSpans.Count(s => s.HasError);
            double errorRate = Math.Round((double)errors / serviceSpans.Count * 100.0, 2);

            string status = "Healthy";
            if (errorRate > 10.0 || serviceAlerts > 2 || p95 > 800)
            {
                status = "Critical";
            }
            else if (errorRate > 3.0 || serviceAlerts > 0 || p95 > 350)
            {
                status = "Degraded";
            }

            summaries.Add(new ServiceHealthSummary
            {
                ServiceName = svc,
                Status = status,
                AvgLatencyMs = Math.Round(avg, 2),
                P95LatencyMs = Math.Round(p95, 2),
                P99LatencyMs = Math.Round(p99, 2),
                ErrorRatePercent = errorRate,
                TotalRequests = serviceSpans.Count,
                ActiveAlertsCount = serviceAlerts,
                LastHeartbeat = _serviceHeartbeats.GetValueOrDefault(svc, DateTimeOffset.UtcNow)
            });
        }

        return summaries;
    }
}
