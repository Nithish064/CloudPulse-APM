using Apm.Shared.Models;
using Apm.Shared.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Apm.IngestionService.Services;

public class IngestionBatchWorker : BackgroundService
{
    private readonly IngestionChannelBuffer _buffer;
    private readonly ITelemetryStore _store;
    private readonly ILogger<IngestionBatchWorker> _logger;
    private const int BatchFlushIntervalMs = 250;
    private const int MaxBatchSize = 1000;

    public IngestionBatchWorker(
        IngestionChannelBuffer buffer,
        ITelemetryStore store,
        ILogger<IngestionBatchWorker> logger)
    {
        _buffer = buffer;
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Ingestion Batch Worker background service started. Processing channels...");

        var spanBatch = new List<TelemetrySpan>(MaxBatchSize);
        var metricBatch = new List<MetricDataPoint>(MaxBatchSize);
        var logBatch = new List<LogRecord>(MaxBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 1. Drain Spans
                while (spanBatch.Count < MaxBatchSize && _buffer.SpanChannel.Reader.TryRead(out var span))
                {
                    spanBatch.Add(span);
                }

                if (spanBatch.Count > 0)
                {
                    _store.AddSpans(spanBatch);
                    _logger.LogDebug("Flushed {Count} telemetry spans to store.", spanBatch.Count);
                    spanBatch.Clear();
                }

                // 2. Drain Metrics
                while (metricBatch.Count < MaxBatchSize && _buffer.MetricChannel.Reader.TryRead(out var metric))
                {
                    metricBatch.Add(metric);
                }

                if (metricBatch.Count > 0)
                {
                    _store.AddMetrics(metricBatch);
                    _logger.LogDebug("Flushed {Count} metrics to store.", metricBatch.Count);
                    metricBatch.Clear();
                }

                // 3. Drain Logs
                while (logBatch.Count < MaxBatchSize && _buffer.LogChannel.Reader.TryRead(out var log))
                {
                    logBatch.Add(log);
                }

                if (logBatch.Count > 0)
                {
                    foreach (var log in logBatch)
                    {
                        _store.AddLog(log);
                    }
                    _logger.LogDebug("Flushed {Count} log records to store.", logBatch.Count);
                    logBatch.Clear();
                }

                await Task.Delay(BatchFlushIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Ingestion Batch Worker is gracefully shutting down.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in IngestionBatchWorker processing loop: {Message}", ex.Message);
                // Pause briefly before retrying loop to avoid fast spin in failure state
                await Task.Delay(500, stoppingToken);
            }
        }
    }
}
