using Azure.Messaging.ServiceBus;

namespace ServiceBusEmulatorExplorer;

public static class Helpers
{
    // The emulator's admin runtime properties always report 0 for message counts, so peek-count instead.
    public static async Task<long> CountMessagesAsync(
        ServiceBusReceiver receiver,
        long maxToCount = 1000,
        TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));

        long count = 0;
        long fromSequenceNumber = 0;
        try
        {
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
        }
        catch (Exception)
        {
            // ignored - best effort count
        }

        return count;
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
