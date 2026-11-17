using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Apm.AnomalyEngineService.Services;

public class AnomalyDetectorWorker : BackgroundService
{
    private readonly StatisticalAnomalyEngine _engine;
    private readonly ILogger<AnomalyDetectorWorker> _logger;
    private const int DetectionIntervalMs = 5000;

    public AnomalyDetectorWorker(
        StatisticalAnomalyEngine engine,
        ILogger<AnomalyDetectorWorker> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Anomaly Detector Worker started. Monitoring telemetry for latency and error anomalies...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var alerts = _engine.EvaluateAnomalies();
                if (alerts.Count > 0)
                {
                    _logger.LogWarning("Anomaly detection cycle identified {Count} new/updated active anomalies.", alerts.Count);
                }
                else
                {
                    _logger.LogDebug("Anomaly detection cycle clean. All monitored services operating within nominal bounds.");
                }

                await Task.Delay(DetectionIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Anomaly Detector Worker gracefully shutting down.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in anomaly detector cycle: {Message}", ex.Message);
                await Task.Delay(2000, stoppingToken);
            }
        }
    }
}
