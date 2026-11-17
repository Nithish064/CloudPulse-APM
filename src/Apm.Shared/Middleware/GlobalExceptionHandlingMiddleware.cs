using System.Net;
using System.Text.Json;
using Apm.Shared.Models;
using Microsoft.AspNetCore.Http;
using Serilog;

namespace Apm.Shared.Middleware;

public class GlobalExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly ILogger Logger = Log.ForContext<GlobalExceptionHandlingMiddleware>();

    public GlobalExceptionHandlingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (BadHttpRequestException badReqEx)
        {
            await HandleExceptionAsync(context, badReqEx, HttpStatusCode.BadRequest, "Invalid request payload format.");
        }
        catch (ArgumentException argEx)
        {
            await HandleExceptionAsync(context, argEx, HttpStatusCode.BadRequest, argEx.Message);
        }
        catch (KeyNotFoundException notFoundEx)
        {
            await HandleExceptionAsync(context, notFoundEx, HttpStatusCode.NotFound, notFoundEx.Message);
        }
        catch (TimeoutException timeoutEx)
        {
            await HandleExceptionAsync(context, timeoutEx, HttpStatusCode.GatewayTimeout, "Operation timed out.");
        }
        catch (Exception unhandledEx)
        {
            await HandleExceptionAsync(context, unhandledEx, HttpStatusCode.InternalServerError, "An unexpected internal server error occurred.");
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception, HttpStatusCode statusCode, string userMessage)
    {
        string correlationId = context.Items.TryGetValue(CorrelationIdMiddleware.CorrelationHeaderKey, out var cId)
            ? cId?.ToString() ?? "N/A"
            : "N/A";

        Logger.Error(exception,
            "Unhandled exception caught in middleware pipeline! StatusCode: {StatusCode} Path: {Path} Method: {Method} CorrelationId: {CorrelationId} Message: {ErrorMessage}",
            (int)statusCode,
            context.Request.Path,
            context.Request.Method,
            correlationId,
            exception.Message);

        try
        {
            if (!context.Response.HasStarted)
            {
                context.Response.ContentType = "application/json";
                context.Response.StatusCode = (int)statusCode;

                var responsePayload = ApiResponse<object>.Fail(
                    message: $"{userMessage} (Detail: {exception.Message})",
                    correlationId: correlationId
                );

                var json = JsonSerializer.Serialize(responsePayload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });

                await context.Response.WriteAsync(json);
            }
        }
        catch (Exception responseEx)
        {
            Logger.Fatal(responseEx, "Failed to write error response to client!");
        }
    }
}
