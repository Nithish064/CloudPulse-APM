namespace Apm.Shared.Models;

public enum AlertSeverity
{
    Info,
    Warning,
    High,
    Critical
}

public class AnomalyAlert
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string MetricName { get; set; } = string.Empty;
    public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;
    public double CurrentValue { get; set; }
    public double BaselineValue { get; set; }
    public double ZScore { get; set; }
    public string Description { get; set; } = string.Empty;
    public string SuggestedRemediation { get; set; } = string.Empty;
    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsAcknowledged { get; set; }
}
