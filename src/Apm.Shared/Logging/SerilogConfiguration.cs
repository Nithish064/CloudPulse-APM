using Apm.Shared.Middleware;
using Microsoft.AspNetCore.Builder;
using Serilog;
using Serilog.Events;

namespace Apm.Shared.Logging;

public static class SerilogConfiguration
{
    public static void ConfigureSerilog(this WebApplicationBuilder builder, string serviceName)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ServiceName", serviceName)
            .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}] [Corr:{CorrelationId}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: $"logs/{serviceName}-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{ServiceName}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        builder.Host.UseSerilog();
    }

    public static IApplicationBuilder UseApmMiddlewares(this IApplicationBuilder app)
    {
        // 1. Correlation ID is first so all downstream middlewares have context
        app.UseMiddleware<CorrelationIdMiddleware>();

        // 2. Global Exception Handling wraps everything in try-catch
        app.UseMiddleware<GlobalExceptionHandlingMiddleware>();

        // 3. Request timing & logging
        app.UseMiddleware<RequestLoggingMiddleware>();

        return app;
    }
}
