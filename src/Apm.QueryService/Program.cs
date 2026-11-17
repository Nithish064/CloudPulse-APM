using System.Text.Json;
using Apm.QueryService.Services;
using Apm.Shared.Logging;
using Apm.Shared.Models;
using Apm.Shared.Storage;
using Microsoft.AspNetCore.Mvc;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure Serilog structured logging
builder.ConfigureSerilog("Apm.QueryService");

// 2. Register DI Services
builder.Services.AddSharedTelemetryStore();
builder.Services.AddSingleton<TrafficSimulator>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors("AllowAll");

// 3. Register custom APM Middlewares (CorrelationId, GlobalExceptionHandling, RequestLogging)
app.UseApmMiddlewares();

// Initialize simulator baseline data
var simulator = app.Services.GetRequiredService<TrafficSimulator>();
simulator.SeedInitialData();

// 4. API Endpoints

app.MapGet("/health", () => Results.Ok(new
{
    service = "Apm.QueryService",
    status = "Healthy",
    timestamp = DateTimeOffset.UtcNow
}));

// Query recent spans
app.MapGet("/api/traces", (
    [FromServices] ITelemetryStore store,
    [FromQuery] string? serviceName,
    [FromQuery] bool? errorOnly,
    [FromQuery] int? limit,
    HttpContext context) =>
{
    try
    {
        var spans = store.GetRecentSpans(limit ?? 100, serviceName, errorOnly);
        string? correlationId = context.Items.TryGetValue(Apm.Shared.Middleware.CorrelationIdMiddleware.CorrelationHeaderKey, out var cId)
            ? cId?.ToString()
            : null;

        return Results.Ok(ApiResponse<IReadOnlyList<TelemetrySpan>>.Ok(spans, correlationId));
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to query traces: {Message}", ex.Message);
        throw;
    }
});

// Query full distributed trace by TraceId
app.MapGet("/api/traces/{traceId}", (
    string traceId,
    [FromServices] ITelemetryStore store,
    HttpContext context) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(traceId))
        {
            return Results.BadRequest(ApiResponse<object>.Fail("TraceId cannot be empty."));
        }

        var traceSpans = store.GetTraceSpans(traceId);
        string? correlationId = context.Items.TryGetValue(Apm.Shared.Middleware.CorrelationIdMiddleware.CorrelationHeaderKey, out var cId)
            ? cId?.ToString()
            : null;

        if (traceSpans == null || traceSpans.Count == 0)
        {
            return Results.NotFound(ApiResponse<object>.Fail($"Trace '{traceId}' was not found.", correlationId));
        }

        return Results.Ok(ApiResponse<IReadOnlyList<TelemetrySpan>>.Ok(traceSpans, correlationId));
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to retrieve trace {TraceId}: {Message}", traceId, ex.Message);
        throw;
    }
});

// Query overall service health & summaries
app.MapGet("/api/metrics/services", (
    [FromServices] ITelemetryStore store,
    HttpContext context) =>
{
    try
    {
        var summaries = store.GetServiceHealthSummaries();
        string? correlationId = context.Items.TryGetValue(Apm.Shared.Middleware.CorrelationIdMiddleware.CorrelationHeaderKey, out var cId)
            ? cId?.ToString()
            : null;

        return Results.Ok(ApiResponse<IReadOnlyList<ServiceHealthSummary>>.Ok(summaries, correlationId));
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to compute service health metrics: {Message}", ex.Message);
        throw;
    }
});

// Query historical metric time-series
app.MapGet("/api/metrics/history", (
    [FromServices] ITelemetryStore store,
    [FromQuery] string serviceName,
    [FromQuery] string? metricName,
    [FromQuery] int? limit,
    HttpContext context) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return Results.BadRequest(ApiResponse<object>.Fail("serviceName query parameter is required."));
        }

        var metrics = store.GetServiceMetrics(serviceName, metricName, limit ?? 60);
        string? correlationId = context.Items.TryGetValue(Apm.Shared.Middleware.CorrelationIdMiddleware.CorrelationHeaderKey, out var cId)
            ? cId?.ToString()
            : null;

        return Results.Ok(ApiResponse<IReadOnlyList<MetricDataPoint>>.Ok(metrics, correlationId));
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to retrieve service metrics history: {Message}", ex.Message);
        throw;
    }
});

// Query logs
app.MapGet("/api/logs", (
    [FromServices] ITelemetryStore store,
    [FromQuery] string? serviceName,
    [FromQuery] string? level,
    [FromQuery] int? limit,
    HttpContext context) =>
{
    try
    {
        var logs = store.GetRecentLogs(limit ?? 100, serviceName, level);
        string? correlationId = context.Items.TryGetValue(Apm.Shared.Middleware.CorrelationIdMiddleware.CorrelationHeaderKey, out var cId)
            ? cId?.ToString()
            : null;

        return Results.Ok(ApiResponse<IReadOnlyList<LogRecord>>.Ok(logs, correlationId));
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to retrieve logs: {Message}", ex.Message);
        throw;
    }
});

// Generate synthetic traffic (Normal, Latency Spike, Error Cascade)
app.MapPost("/api/simulator/generate", (
    [FromQuery] string? scenario,
    [FromServices] TrafficSimulator sim,
    [FromServices] ILogger<Program> logger,
    HttpContext context) =>
{
    try
    {
        string mode = scenario?.ToLowerInvariant() switch
        {
            "latency_spike" => "latency_spike",
            "error_cascade" => "error_cascade",
            _ => "normal"
        };

        var generatedSpans = sim.GenerateSyntheticTrace(mode);
        logger.LogInformation("TrafficSimulator generated scenario '{Mode}' with {Count} spans.", mode, generatedSpans.Count);

        string? correlationId = context.Items.TryGetValue(Apm.Shared.Middleware.CorrelationIdMiddleware.CorrelationHeaderKey, out var cId)
            ? cId?.ToString()
            : null;

        return Results.Ok(ApiResponse<object>.Ok(
            new { scenario = mode, traceId = generatedSpans[0].TraceId, spanCount = generatedSpans.Count },
            correlationId,
            $"Generated '{mode}' synthetic trace."));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to generate synthetic traffic: {Message}", ex.Message);
        throw;
    }
});

// Server-Sent Events (SSE) Live Feed for real-time dashboard updates
app.MapGet("/api/live/stream", async (
    HttpContext httpContext,
    [FromServices] ITelemetryStore store,
    CancellationToken ct) =>
{
    httpContext.Response.Headers.Append("Content-Type", "text/event-stream");
    httpContext.Response.Headers.Append("Cache-Control", "no-cache");
    httpContext.Response.Headers.Append("Connection", "keep-alive");

    try
    {
        while (!ct.IsCancellationRequested)
        {
            var summaries = store.GetServiceHealthSummaries();
            var recentAlerts = store.GetAlerts(5, unacknowledgedOnly: true);
            var recentSpans = store.GetRecentSpans(10);

            var payload = new
            {
                services = summaries,
                alerts = recentAlerts,
                spans = recentSpans,
                timestamp = DateTimeOffset.UtcNow
            };

            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await httpContext.Response.WriteAsync($"data: {json}\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);

            await Task.Delay(2000, ct);
        }
    }
    catch (OperationCanceledException)
    {
        // Client disconnected normally
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "SSE stream connection terminated: {Message}", ex.Message);
    }
});

app.Run();
