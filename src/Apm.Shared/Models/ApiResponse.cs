namespace Apm.Shared.Models;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Message { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public static ApiResponse<T> Ok(T data, string? correlationId = null, string? message = null) =>
        new()
        {
            Success = true,
            Data = data,
            CorrelationId = correlationId,
            Message = message
        };

    public static ApiResponse<T> Fail(string message, string? correlationId = null) =>
        new()
        {
            Success = false,
            Data = default,
            Message = message,
            CorrelationId = correlationId
        };
}
