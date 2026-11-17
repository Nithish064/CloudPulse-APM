namespace Apm.Shared.Models;

public class MetricDataPoint
{
    public string MetricName { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Unit { get; set; } = "ms";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<string, string> Dimensions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
