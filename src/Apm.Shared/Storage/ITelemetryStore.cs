using Apm.Shared.Models;

namespace Apm.Shared.Storage;

public interface ITelemetryStore
{
    void AddSpan(TelemetrySpan span);
    void AddSpans(IEnumerable<TelemetrySpan> spans);
    void AddMetric(MetricDataPoint metric);
    void AddMetrics(IEnumerable<MetricDataPoint> metrics);
    void AddLog(LogRecord log);
    void AddAlert(AnomalyAlert alert);

    IReadOnlyList<TelemetrySpan> GetRecentSpans(int limit = 100, string? serviceFilter = null, bool? errorOnly = null);
    IReadOnlyList<TelemetrySpan>? GetTraceSpans(string traceId);
    IReadOnlyList<MetricDataPoint> GetServiceMetrics(string serviceName, string? metricName = null, int limit = 60);
    IReadOnlyList<LogRecord> GetRecentLogs(int limit = 100, string? serviceFilter = null, string? levelFilter = null);
    IReadOnlyList<AnomalyAlert> GetAlerts(int limit = 50, bool unacknowledgedOnly = false);
    IReadOnlyList<ServiceHealthSummary> GetServiceHealthSummaries();
    bool AcknowledgeAlert(string alertId);
}
