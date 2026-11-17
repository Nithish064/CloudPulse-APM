namespace Apm.Shared.Models;

public class ServiceHealthSummary
{
    public string ServiceName { get; set; } = string.Empty;
    public string Status { get; set; } = "Healthy"; // Healthy, Degraded, Critical
    public double AvgLatencyMs { get; set; }
    public double P95LatencyMs { get; set; }
    public double P99LatencyMs { get; set; }
    public double ErrorRatePercent { get; set; }
    public long TotalRequests { get; set; }
    public int ActiveAlertsCount { get; set; }
    public DateTimeOffset LastHeartbeat { get; set; } = DateTimeOffset.UtcNow;
}
