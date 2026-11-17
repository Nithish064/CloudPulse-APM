using Apm.Shared.Models;
using Apm.Shared.Storage;
using Microsoft.Extensions.Logging;

namespace Apm.QueryService.Services;

public class TrafficSimulator
{
    private readonly ITelemetryStore _store;
    private readonly ILogger<TrafficSimulator> _logger;

    public TrafficSimulator(ITelemetryStore store, ILogger<TrafficSimulator> logger)
    {
        _store = store;
        _logger = logger;
    }

    public void SeedInitialData()
    {
        try
        {
            _logger.LogInformation("Seeding initial APM baseline telemetry...");
            for (int i = 0; i < 20; i++)
            {
                GenerateSyntheticTrace(scenario: "normal");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed initial telemetry: {Message}", ex.Message);
        }
    }

    public List<TelemetrySpan> GenerateSyntheticTrace(string scenario = "normal")
    {
        var spans = new List<TelemetrySpan>();
        string traceId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow.AddSeconds(-Random.Shared.Next(1, 300));

        bool injectSlowOrder = scenario == "latency_spike";
        bool injectPaymentError = scenario == "error_cascade";

        // 1. Gateway Span (Root)
        double gwDuration = injectSlowOrder ? Random.Shared.Next(650, 1100) : Random.Shared.Next(40, 95);
        var gwSpan = new TelemetrySpan
        {
            TraceId = traceId,
            SpanId = Guid.NewGuid().ToString("N")[..16],
            ParentSpanId = null,
            ServiceName = "api-gateway",
            OperationName = "POST /api/v1/orders/checkout",
            StartTime = now,
            DurationMs = gwDuration,
            StatusCode = injectPaymentError ? 500 : 200,
            HasError = injectPaymentError,
            ErrorMessage = injectPaymentError ? "Downstream dependency failure: Payment service unreachable" : null,
            Attributes = new Dictionary<string, string>
            {
                ["http.method"] = "POST",
                ["http.route"] = "/api/v1/orders/checkout",
                ["client.ip"] = "192.168.1.104"
            }
        };
        spans.Add(gwSpan);

        // 2. Order Service Span (Child of Gateway)
        var orderStart = now.AddMilliseconds(5);
        double orderDuration = injectSlowOrder ? gwDuration - 20 : Random.Shared.Next(30, 60);
        var orderSpan = new TelemetrySpan
        {
            TraceId = traceId,
            SpanId = Guid.NewGuid().ToString("N")[..16],
            ParentSpanId = gwSpan.SpanId,
            ServiceName = "order-service",
            OperationName = "OrderCommandHandler.HandleCheckout",
            StartTime = orderStart,
            DurationMs = orderDuration,
            StatusCode = injectPaymentError ? 500 : 200,
            HasError = injectPaymentError,
            ErrorMessage = injectPaymentError ? "PaymentGatewayException: Timeout waiting for bank confirmation" : null,
            Attributes = new Dictionary<string, string>
            {
                ["order.id"] = $"ORD-{Random.Shared.Next(10000, 99999)}",
                ["customer.tier"] = "Premium"
            }
        };
        spans.Add(orderSpan);

        // 3. Inventory Service Span (Child of Order Service)
        var invStart = orderStart.AddMilliseconds(5);
        double invDuration = Random.Shared.Next(10, 25);
        var invSpan = new TelemetrySpan
        {
            TraceId = traceId,
            SpanId = Guid.NewGuid().ToString("N")[..16],
            ParentSpanId = orderSpan.SpanId,
            ServiceName = "inventory-service",
            OperationName = "InventoryRepository.ReserveStockAsync",
            StartTime = invStart,
            DurationMs = invDuration,
            StatusCode = 200,
            HasError = false,
            Attributes = new Dictionary<string, string>
            {
                ["db.system"] = "postgresql",
                ["db.table"] = "sku_reservations",
                ["items.count"] = "3"
            }
        };
        spans.Add(invSpan);

        // 4. Payment Gateway Span (Child of Order Service)
        var payStart = invStart.AddMilliseconds(invDuration + 5);
        double payDuration = injectSlowOrder ? Random.Shared.Next(500, 950) : Random.Shared.Next(20, 50);
        var paySpan = new TelemetrySpan
        {
            TraceId = traceId,
            SpanId = Guid.NewGuid().ToString("N")[..16],
            ParentSpanId = orderSpan.SpanId,
            ServiceName = "payment-gateway",
            OperationName = "StripeConnector.ChargeCreditCardAsync",
            StartTime = payStart,
            DurationMs = payDuration,
            StatusCode = injectPaymentError ? 504 : 200,
            HasError = injectPaymentError,
            ErrorMessage = injectPaymentError ? "Gateway timeout 504: Bank authorization host connection closed prematurely" : null,
            Attributes = new Dictionary<string, string>
            {
                ["payment.provider"] = "Stripe",
                ["payment.currency"] = "USD",
                ["payment.amount"] = "149.99"
            }
        };
        spans.Add(paySpan);

        _store.AddSpans(spans);

        // Store corresponding metrics and logs
        foreach (var span in spans)
        {
            _store.AddMetric(new MetricDataPoint
            {
                ServiceName = span.ServiceName,
                MetricName = "request_latency_ms",
                Value = span.DurationMs,
                Timestamp = span.StartTime
            });

            if (span.HasError)
            {
                _store.AddLog(new LogRecord
                {
                    Timestamp = span.StartTime,
                    Level = "Error",
                    ServiceName = span.ServiceName,
                    Message = span.ErrorMessage ?? "Span completed with error status",
                    TraceId = span.TraceId,
                    SpanId = span.SpanId,
                    Exception = span.ErrorMessage
                });
            }
        }

        return spans;
    }
}
