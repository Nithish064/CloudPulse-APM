using Apm.IngestionService.Services;
using Apm.Shared.Logging;
using Apm.Shared.Models;
using Apm.Shared.Storage;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure Serilog structured logging
builder.ConfigureSerilog("Apm.IngestionService");

// 2. Register Dependency Injection Services
builder.Services.AddSharedTelemetryStore();
builder.Services.AddSingleton<IngestionChannelBuffer>();
builder.Services.AddHostedService<IngestionBatchWorker>();

var app = builder.Build();

// 3. Register custom APM Middlewares (CorrelationId, GlobalExceptionHandling, RequestLogging)
app.UseApmMiddlewares();

// 4. Ingestion Endpoints with rigorous try-catch validation

app.MapGet("/health", () => Results.Ok(new
{
    service = "Apm.IngestionService",
    status = "Healthy",
    timestamp = DateTimeOffset.UtcNow
}));

// Ingest Spans (Single or Batch)
app.MapPost("/api/ingest/spans", async (
    [FromBody] List<TelemetrySpan> spans,
    [FromServices] IngestionChannelBuffer buffer,
    [FromServices] ILogger<Program> logger,
    HttpContext context) =>
{
    try
    {
        if (spans == null || spans.Count == 0)
        {
            logger.LogWarning("Ingest spans invoked with empty payload.");
            return Results.BadRequest(ApiResponse<object>.Fail("Span batch cannot be empty."));
        }

        int queuedCount = 0;
        foreach (var span in spans)
        {
            // Basic validation
            if (string.IsNullOrWhiteSpace(span.ServiceName))
            {
                span.ServiceName = "unknown-service";
            }
            if (string.IsNullOrWhiteSpace(span.OperationName))
            {
                span.OperationName = "unspecified-operation";
            }

            if (buffer.SpanChannel.Writer.TryWrite(span))
            {
                queuedCount++;
            }
        }

        logger.LogInformation("Enqueued {Count}/{Total} spans for asynchronous ingestion.", queuedCount, spans.Count);

        string? correlationId = context.Items.TryGetValue(Apm.Shared.Middleware.CorrelationIdMiddleware.CorrelationHeaderKey, out var cId)
            ? cId?.ToString()
            : null;

        return Results.Accepted(
            value: ApiResponse<object>.Ok(
                new { queued = queuedCount, total = spans.Count },
                correlationId: correlationId,
                message: $"Successfully queued {queuedCount} spans for processing."));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to ingest telemetry spans: {Message}", ex.Message);
        throw; // Let GlobalExceptionHandlingMiddleware formulate standard RFC error response
    }
});

// Ingest Metrics (Single or Batch)
app.MapPost("/api/ingest/metrics", async (
    [FromBody] List<MetricDataPoint> metrics,
    [FromServices] IngestionChannelBuffer buffer,
    [FromServices] ILogger<Program> logger,
    HttpContext context) =>
{
    try
    {
        if (metrics == null || metrics.Count == 0)
        {
            return Results.BadRequest(ApiResponse<object>.Fail("Metrics batch cannot be empty."));
        }

        int queuedCount = 0;
        foreach (var metric in metrics)
        {
            if (buffer.MetricChannel.Writer.TryWrite(metric))
            {
                queuedCount++;
            }
        }

        logger.LogInformation("Enqueued {Count}/{Total} metrics.", queuedCount, metrics.Count);

        string? correlationId = context.Items.TryGetValue(Apm.Shared.Middleware.CorrelationIdMiddleware.CorrelationHeaderKey, out var cId)
            ? cId?.ToString()
            : null;

        return Results.Accepted(
            value: ApiResponse<object>.Ok(
                new { queued = queuedCount, total = metrics.Count },
                correlationId: correlationId));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to ingest metrics: {Message}", ex.Message);
        throw;
    }
});

// Ingest Logs (Single or Batch)
app.MapPost("/api/ingest/logs", async (
    [FromBody] List<LogRecord> logs,
    [FromServices] IngestionChannelBuffer buffer,
    [FromServices] ILogger<Program> logger,
    HttpContext context) =>
{
    try
    {
        if (logs == null || logs.Count == 0)
        {
            return Results.BadRequest(ApiResponse<object>.Fail("Log batch cannot be empty."));
        }

        int queuedCount = 0;
        foreach (var log in logs)
        {
            if (buffer.LogChannel.Writer.TryWrite(log))
            {
                queuedCount++;
            }
        }

        logger.LogInformation("Enqueued {Count}/{Total} log records.", queuedCount, logs.Count);

        string? correlationId = context.Items.TryGetValue(Apm.Shared.Middleware.CorrelationIdMiddleware.CorrelationHeaderKey, out var cId)
            ? cId?.ToString()
            : null;

        return Results.Accepted(
            value: ApiResponse<object>.Ok(
                new { queued = queuedCount, total = logs.Count },
                correlationId: correlationId));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to ingest logs: {Message}", ex.Message);
        throw;
    }
});

app.Run();
