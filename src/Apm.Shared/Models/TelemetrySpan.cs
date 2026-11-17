namespace Apm.Shared.Models;

public class TelemetrySpan
{
    public string TraceId { get; set; } = Guid.NewGuid().ToString("N");
    public string SpanId { get; set; } = Guid.NewGuid().ToString("N")[..16];
    public string? ParentSpanId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string OperationName { get; set; } = string.Empty;
    public DateTimeOffset StartTime { get; set; } = DateTimeOffset.UtcNow;
    public double DurationMs { get; set; }
    public int StatusCode { get; set; } = 200;
    public bool HasError { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, string> Attributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
