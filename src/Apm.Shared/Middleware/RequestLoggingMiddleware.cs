using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Serilog;

namespace Apm.Shared.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly ILogger Logger = Log.ForContext<RequestLoggingMiddleware>();
    private const double SlowRequestThresholdMs = 400.0;

    public RequestLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        string method = context.Request.Method;
        string path = context.Request.Path;
        string? queryString = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null;

        Logger.Information("--> HTTP {Method} {Path}{QueryString}", method, path, queryString ?? "");

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            double elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
            int statusCode = context.Response.StatusCode;

            try
            {
                if (elapsedMs > SlowRequestThresholdMs)
                {
                    Logger.Warning(
                        "<-- [SLOW REQUEST DETECTED] HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs:0.00} ms (Threshold: {ThresholdMs} ms)",
                        method, path, statusCode, elapsedMs, SlowRequestThresholdMs);
                }
                else
                {
                    Logger.Information(
                        "<-- HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs:0.00} ms",
                        method, path, statusCode, elapsedMs);
                }
            }
            catch (Exception ex)
            {
                // Never let logging failure break the response pipeline
                Console.WriteLine($"[Logger Error] {ex.Message}");
            }
        }
    }
}
