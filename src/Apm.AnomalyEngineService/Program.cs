using Apm.AnomalyEngineService.Services;
using Apm.Shared.Logging;
using Apm.Shared.Models;
using Apm.Shared.Storage;
using Microsoft.AspNetCore.Mvc;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure Serilog structured logging
builder.ConfigureSerilog("Apm.AnomalyEngineService");

// 2. Register DI Services
builder.Services.AddSharedTelemetryStore();
builder.Services.AddSingleton<StatisticalAnomalyEngine>();
builder.Services.AddHostedService<AnomalyDetectorWorker>();

var app = builder.Build();

// 3. Register custom APM Middlewares (CorrelationId, GlobalExceptionHandling, RequestLogging)
app.UseApmMiddlewares();

// 4. API Endpoints

app.MapGet("/health", () => Results.Ok(new
{
    service = "Apm.AnomalyEngineService",
    status = "Healthy",
    timestamp = DateTimeOffset.UtcNow
}));

// Query all detected anomalies
app.MapGet("/api/anomalies", (
    [FromServices] ITelemetryStore store,
    [FromQuery] bool unacknowledgedOnly,
    [FromQuery] int? limit,
    HttpContext context) =>
{
    try
    {
        var alerts = store.GetAlerts(limit ?? 50, unacknowledgedOnly);

        string? correlationId = context.Items.TryGetValue(Apm.Shared.Middleware.CorrelationIdMiddleware.CorrelationHeaderKey, out var cId)
            ? cId?.ToString()
            : null;

        return Results.Ok(ApiResponse<IReadOnlyList<AnomalyAlert>>.Ok(alerts, correlationId));
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to retrieve anomaly alerts: {Message}", ex.Message);
        throw;
    }
});

// Trigger manual anomaly evaluation
app.MapPost("/api/anomalies/evaluate", (
    [FromServices] StatisticalAnomalyEngine engine,
    [FromServices] ILogger<Program> logger,
    HttpContext context) =>
{
    try
    {
        logger.LogInformation("Manual anomaly evaluation requested.");
        var detected = engine.EvaluateAnomalies();

        string? correlationId = context.Items.TryGetValue(Apm.Shared.Middleware.CorrelationIdMiddleware.CorrelationHeaderKey, out var cId)
            ? cId?.ToString()
            : null;

        return Results.Ok(ApiResponse<object>.Ok(
            new { evaluatedCount = detected.Count, alerts = detected },
            correlationId,
            $"Evaluation completed. {detected.Count} anomalies flagged."));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed during manual anomaly evaluation: {Message}", ex.Message);
        throw;
    }
});

// Acknowledge an alert incident
app.MapPost("/api/anomalies/{id}/acknowledge", (
    string id,
    [FromServices] ITelemetryStore store,
    [FromServices] ILogger<Program> logger,
    HttpContext context) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Results.BadRequest(ApiResponse<object>.Fail("Alert ID is required."));
        }

        bool updated = store.AcknowledgeAlert(id);
        if (!updated)
        {
            return Results.NotFound(ApiResponse<object>.Fail($"Alert with ID '{id}' was not found."));
        }

        logger.LogInformation("Alert incident {AlertId} was acknowledged.", id);

        string? correlationId = context.Items.TryGetValue(Apm.Shared.Middleware.CorrelationIdMiddleware.CorrelationHeaderKey, out var cId)
            ? cId?.ToString()
            : null;

        return Results.Ok(ApiResponse<object>.Ok(new { acknowledged = true, id }, correlationId, "Alert acknowledged successfully."));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error acknowledging alert {AlertId}: {Message}", id, ex.Message);
        throw;
    }
});

app.Run();
