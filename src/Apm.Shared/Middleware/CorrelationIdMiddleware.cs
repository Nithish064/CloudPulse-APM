using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace Apm.Shared.Middleware;

public class CorrelationIdMiddleware
{
    public const string CorrelationHeaderKey = "X-Correlation-ID";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate _next)
    {
        this._next = _next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string correlationId;

        // Try-catch block to handle edge cases in header parsing gracefully
        try
        {
            if (context.Request.Headers.TryGetValue(CorrelationHeaderKey, out var headerValue) 
                && !string.IsNullOrWhiteSpace(headerValue))
            {
                correlationId = headerValue.ToString();
            }
            else
            {
                correlationId = Guid.NewGuid().ToString("N");
            }
        }
        catch (Exception)
        {
            correlationId = Guid.NewGuid().ToString("N");
        }

        context.Items[CorrelationHeaderKey] = correlationId;

        // Ensure outgoing response carries the correlation ID
        context.Response.OnStarting(() =>
        {
            try
            {
                if (!context.Response.Headers.ContainsKey(CorrelationHeaderKey))
                {
                    context.Response.Headers[CorrelationHeaderKey] = correlationId;
                }
            }
            catch
            {
                // Headers may already be committed; ignore safely
            }
            return Task.CompletedTask;
        });

        // Enrich all Serilog log statements within this execution scope with the CorrelationId
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
