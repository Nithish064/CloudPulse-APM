using Apm.Gateway.Middleware;
using Apm.Shared.Logging;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure Serilog structured logging
builder.ConfigureSerilog("Apm.Gateway");

// 2. Register HTTP client for downstream proxying
builder.Services.AddHttpClient("GatewayDownstreamClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

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

// 4. Static Files & SPA Dashboard Host
app.UseDefaultFiles();
app.UseStaticFiles();

// 5. Dynamic Gateway Reverse Proxy to Ingestion, Anomaly, and Query microservices
app.UseMiddleware<GatewayReverseProxyMiddleware>();

// Health endpoint
app.MapGet("/health", () => Results.Ok(new
{
    service = "Apm.Gateway",
    status = "Healthy",
    timestamp = DateTimeOffset.UtcNow
}));

// Fallback to React SPA index.html
app.MapFallbackToFile("index.html");

app.Run();
