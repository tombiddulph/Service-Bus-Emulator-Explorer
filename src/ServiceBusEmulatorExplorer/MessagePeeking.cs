using Azure.Messaging.ServiceBus;

namespace ServiceBusEmulatorExplorer;

internal sealed record PeekResult(IReadOnlyList<ServiceBusReceivedMessage> Messages, bool HasMore, string? NextSequenceNumber);

internal static class MessagePeeking
{
    public static Dictionary<string, string[]> ValidatePagination(int skip, int take)
    {
        var errors = new Dictionary<string, string[]>();
        if (skip < 0 || skip > 10000)
            errors["skip"] = ["Skip must be between 0 and 10000."];
        if (take < 1 || take > 250)
            errors["take"] = ["Take must be between 1 and 250."];
        return errors;
    }

    public static async Task<PeekResult> PeekAsync(
        ServiceBusEndpointCache endpointCache, ServiceBusReceiver receiver, int skip, int take,
        CancellationToken cancellationToken, long? cursor = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await using var operationLock = await endpointCache.LockAsync(receiver, deadline.Token);
            long fromSequenceNumber = cursor ?? 0;
            var remaining = cursor.HasValue ? 0 : skip;
            while (remaining > 0)
            {
                var skipped = await receiver.PeekMessagesAsync(Math.Min(remaining, 250), fromSequenceNumber, deadline.Token);
                // Do not restart at the beginning when the requested offset exceeds the available messages.
                if (skipped.Count == 0) return new PeekResult([], false, null);
                remaining -= skipped.Count;
                fromSequenceNumber = skipped[^1].SequenceNumber + 1;
            }

            var messages = new List<ServiceBusReceivedMessage>();
            while (messages.Count <= take)
            {
                var batch = await receiver.PeekMessagesAsync(take + 1 - messages.Count, fromSequenceNumber, deadline.Token);
                if (batch.Count == 0) break;
                messages.AddRange(batch);
                fromSequenceNumber = batch[^1].SequenceNumber + 1;
            }
            var hasMore = messages.Count > take;
            if (hasMore) messages.RemoveAt(messages.Count - 1);
            return new PeekResult(messages, hasMore, hasMore
                ? (messages[^1].SequenceNumber + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Message peek timed out. Try again.", exception);
        }
    }
}
