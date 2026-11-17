using System.Threading.Channels;
using Apm.Shared.Models;

namespace Apm.IngestionService.Services;

public class IngestionChannelBuffer
{
    private const int BoundedCapacity = 50_000;

    public Channel<TelemetrySpan> SpanChannel { get; } = Channel.CreateBounded<TelemetrySpan>(
        new BoundedChannelOptions(BoundedCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true
        });

    public Channel<MetricDataPoint> MetricChannel { get; } = Channel.CreateBounded<MetricDataPoint>(
        new BoundedChannelOptions(BoundedCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true
        });

    public Channel<LogRecord> LogChannel { get; } = Channel.CreateBounded<LogRecord>(
        new BoundedChannelOptions(BoundedCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true
        });
}
