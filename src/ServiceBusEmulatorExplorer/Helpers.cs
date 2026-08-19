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

                if (batch.Count < 100)
                {
                    break;
                }
            }

            // Loop exited because the cap was hit, not because the queue was exhausted - there may be more.
            if (count >= maxToCount)
            {
                isExact = false;
            }
        }
        catch (Exception)
        {
            // best-effort count; whatever was scanned before the timeout/error is a lower bound, not exact
            isExact = false;
        }

        return new MessageCountResult(count, isExact);
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
