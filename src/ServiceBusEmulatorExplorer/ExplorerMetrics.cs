using System.Diagnostics.Metrics;

namespace ServiceBusEmulatorExplorer;

internal static class ExplorerMetrics
{
    public const string MeterName = "ServiceBusEmulatorExplorer";
    private static readonly Meter Meter = new(MeterName);
    public static readonly Histogram<double> CountDuration = Meter.CreateHistogram<double>("explorer.count.duration", "ms");
    public static readonly Histogram<double> LockWait = Meter.CreateHistogram<double>("explorer.receiver.lock_wait", "ms");
    public static readonly Counter<long> BulkOperations = Meter.CreateCounter<long>("explorer.bulk.operations");
}
