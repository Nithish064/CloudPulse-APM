using System.Net;
using Apm.Shared.Middleware;
using Apm.Shared.Models;
using Microsoft.AspNetCore.Http;
using Serilog;

namespace Apm.Gateway.Middleware;

public class GatewayReverseProxyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHttpClientFactory _clientFactory;
    private static readonly Serilog.ILogger Logger = Log.ForContext<GatewayReverseProxyMiddleware>();

    // Route service map
    private const string IngestionServiceUrl = "http://localhost:5001";
    private const string AnomalyEngineServiceUrl = "http://localhost:5002";
    private const string QueryServiceUrl = "http://localhost:5003";

    public GatewayReverseProxyMiddleware(RequestDelegate next, IHttpClientFactory clientFactory)
    {
        _next = next;
        _clientFactory = clientFactory;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Only proxy /api/ routes; allow other routes to fall through to static SPA UI
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        string targetBaseUrl = DetermineDownstreamTarget(path);
        if (string.IsNullOrEmpty(targetBaseUrl))
        {
            await _next(context);
            return;
        }

        string correlationId = context.Items.TryGetValue(CorrelationIdMiddleware.CorrelationHeaderKey, out var cId)
            ? cId?.ToString() ?? Guid.NewGuid().ToString("N")
            : Guid.NewGuid().ToString("N");

        try
        {
            var downstreamUri = new Uri($"{targetBaseUrl}{path}{context.Request.QueryString}");
            Logger.Information("[GATEWAY ROUTING] Forwarding {Method} {Path} -> {DownstreamUri} [Corr: {CorrelationId}]",
                context.Request.Method, path, downstreamUri, correlationId);

            var httpClient = _clientFactory.CreateClient("GatewayDownstreamClient");
            using var forwardRequest = new HttpRequestMessage(new HttpMethod(context.Request.Method), downstreamUri);

            // Forward content if present
            if (HttpMethods.IsPost(context.Request.Method) || 
                HttpMethods.IsPut(context.Request.Method) || 
                HttpMethods.IsPatch(context.Request.Method))
            {
                forwardRequest.Content = new StreamContent(context.Request.Body);
                if (context.Request.ContentType != null)
                {
                    forwardRequest.Content.Headers.ContentType = 
                        System.Net.Http.Headers.MediaTypeHeaderValue.Parse(context.Request.ContentType);
                }
            }

            // Propagate correlation ID header
            forwardRequest.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.CorrelationHeaderKey, correlationId);

            // Forward incoming headers
            foreach (var header in context.Request.Headers)
            {
                if (!header.Key.StartsWith("Host", StringComparison.OrdinalIgnoreCase) &&
                    !header.Key.StartsWith("Content-Length", StringComparison.OrdinalIgnoreCase) &&
                    !header.Key.StartsWith("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    forwardRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                }
            }

            using var response = await httpClient.SendAsync(
                forwardRequest,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted);

            context.Response.StatusCode = (int)response.StatusCode;

            foreach (var header in response.Headers)
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
            foreach (var header in response.Content.Headers)
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }

            // Remove chunking transfer header if set
            context.Response.Headers.Remove("transfer-encoding");

            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (HttpRequestException httpEx)
        {
            Logger.Error(httpEx, "[GATEWAY ERROR] Downstream service at {TargetUrl} is unreachable or refused connection. CorrId: {CorrelationId}",
                targetBaseUrl, correlationId);

            context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(
                $"Downstream service at {targetBaseUrl} is currently offline or unreachable. Please ensure microservice is started. (Detail: {httpEx.Message})",
                correlationId));
        }
        catch (TaskCanceledException)
        {
            Logger.Warning("[GATEWAY TIMEOUT] Downstream call to {TargetUrl} timed out or request was aborted.", targetBaseUrl);
            context.Response.StatusCode = (int)HttpStatusCode.GatewayTimeout;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("Downstream request timed out.", correlationId));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "[GATEWAY FATAL] Unexpected gateway error routing {Path}: {Message}", path, ex.Message);
            throw;
        }
    }

    private static string DetermineDownstreamTarget(string path)
    {
        if (path.StartsWith("/api/ingest", StringComparison.OrdinalIgnoreCase))
        {
            return IngestionServiceUrl;
        }

        if (path.StartsWith("/api/anomalies", StringComparison.OrdinalIgnoreCase))
        {
            return AnomalyEngineServiceUrl;
        }

        if (path.StartsWith("/api/traces", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/metrics", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/logs", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/simulator", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/live", StringComparison.OrdinalIgnoreCase))
        {
            return QueryServiceUrl;
        }

        return string.Empty;
    }
}
