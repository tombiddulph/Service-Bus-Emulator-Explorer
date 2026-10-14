using Azure.Messaging.ServiceBus;

namespace ServiceBusEmulatorExplorer;

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

    public static async Task<IReadOnlyList<ServiceBusReceivedMessage>> PeekAsync(
        ServiceBusEndpointCache endpointCache, ServiceBusReceiver receiver, int skip, int take,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await using var operationLock = await endpointCache.LockAsync(receiver, deadline.Token);
            long fromSequenceNumber = 0;
            var remaining = skip;
            while (remaining > 0)
            {
                var skipped = await receiver.PeekMessagesAsync(Math.Min(remaining, 250), fromSequenceNumber, deadline.Token);
                // Do not restart at the beginning when the requested offset exceeds the available messages.
                if (skipped.Count == 0) return [];
                remaining -= skipped.Count;
                fromSequenceNumber = skipped[^1].SequenceNumber + 1;
            }

            return await receiver.PeekMessagesAsync(take, fromSequenceNumber, deadline.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Message peek timed out. Try again.", exception);
        }
    }
}
