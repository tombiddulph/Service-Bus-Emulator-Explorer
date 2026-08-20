using System.Text.Json;
using Azure.Messaging.ServiceBus;

namespace ServiceBusEmulatorExplorer;

// IsExact is false when the scan hit maxToCount (there may be more beyond it) or was cut short by a timeout/error.
public readonly record struct MessageCountResult(long Count, bool IsExact);

public static class Helpers
{
    // The emulator's admin runtime properties always report 0 for message counts, so peek-count instead.
    public static async Task<MessageCountResult> CountMessagesAsync(
        ServiceBusEndpointCache endpointCache,
        ServiceBusReceiver receiver,
        long maxToCount = 1000,
        TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));

        long count = 0;
        long fromSequenceNumber = 0;
        var isExact = true;
        try
        {
            await using var _ = await endpointCache.LockAsync(receiver, cts.Token);

            // maxMessages is only an upper bound, so a short batch does not imply exhaustion - keep
            // going until an actually empty batch is returned.
            while (count < maxToCount)
            {
                var batch = await receiver.PeekMessagesAsync(
                    maxMessages: 100, fromSequenceNumber: fromSequenceNumber, cancellationToken: cts.Token);

                if (batch.Count == 0)
                {
                    break;
                }

                count += batch.Count;
                fromSequenceNumber = batch[^1].SequenceNumber + 1;
            }

            // Cap reached: probe one more message to know whether the queue is actually exhausted.
            if (count >= maxToCount)
            {
                var probe = await receiver.PeekMessagesAsync(
                    maxMessages: 1, fromSequenceNumber: fromSequenceNumber, cancellationToken: cts.Token);
                isExact = probe.Count == 0;
            }
        }
        catch (Exception)
        {
            // best-effort count; whatever was scanned before the timeout/error is a lower bound, not exact
            isExact = false;
        }

        return new MessageCountResult(count, isExact);
    }

    // Drains every message off the given receiver (active or dead-letter) using ReceiveAndDelete,
    // for "purge all" style operations. Best-effort: whatever isn't drained before the timeout stays put.
    public static async Task PurgeMessagesAsync(
        ServiceBusEndpointCache endpointCache,
        ServiceBusReceiver receiver,
        TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
        try
        {
            await using var _ = await endpointCache.LockAsync(receiver, cts.Token);

            while (!cts.IsCancellationRequested)
            {
                var batch = await receiver.ReceiveMessagesAsync(
                    maxMessages: 100,
                    maxWaitTime: TimeSpan.FromSeconds(1),
                    cancellationToken: cts.Token);

                if (batch.Count == 0)
                    break;
            }
        }
        catch (Exception)
        {
            // best-effort purge; the timeout/cancellation that ends the drain loop lands here too
        }
    }

    // JsonElement values from request bodies can't be written directly as AMQP application properties;
    // convert to the closest supported CLR primitive and reject shapes that have no AMQP equivalent.
    public static bool TryConvertApplicationProperty(JsonElement element, out object? value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                value = element.GetString();
                return true;
            case JsonValueKind.True:
            case JsonValueKind.False:
                value = element.GetBoolean();
                return true;
            case JsonValueKind.Number:
                value = element.TryGetInt64(out var longValue) ? longValue : element.GetDouble();
                return true;
            default:
                value = null;
                return false;
        }
    }
}

public readonly record struct CaseInsensitiveEnum<T>(T Value) where T : struct, Enum
{
    public static bool TryParse(string? value, out CaseInsensitiveEnum<T> result)
    {
        if (Enum.TryParse<T>(value, ignoreCase: true, out var parsed))
        {
            result = new CaseInsensitiveEnum<T>(parsed);
            return true;
        }
        result = default;
        return false;
    }
    
    public static implicit operator T(CaseInsensitiveEnum<T> e) => e.Value;
}
