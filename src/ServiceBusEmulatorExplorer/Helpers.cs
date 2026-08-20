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
    public static async Task<PurgeResult> PurgeMessagesAsync(
        ServiceBusEndpointCache endpointCache,
        ServiceBusReceiver receiver,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
        var removedCount = 0;
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

                removedCount += batch.Count;
            }

            return new PurgeResult(PurgeStatus.Completed, removedCount);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new PurgeResult(PurgeStatus.TimedOut, removedCount,
                "The purge timed out before the receiver was fully drained. Some active messages may remain.");
        }
        catch (ServiceBusException exception) when (exception.Reason == ServiceBusFailureReason.ServiceTimeout)
        {
            return new PurgeResult(PurgeStatus.TimedOut, removedCount,
                "Service Bus timed out before the receiver was fully drained. Some active messages may remain.");
        }
        catch (UnauthorizedAccessException)
        {
            return new PurgeResult(PurgeStatus.Unauthorized, removedCount,
                "The configured Service Bus credentials are not authorized to receive and delete messages.");
        }
        catch (ServiceBusException exception) when (exception.InnerException is UnauthorizedAccessException)
        {
            return new PurgeResult(PurgeStatus.Unauthorized, removedCount,
                "The configured Service Bus credentials are not authorized to receive and delete messages.");
        }
        catch (Exception exception)
        {
            return new PurgeResult(PurgeStatus.Failed, removedCount,
                $"The purge stopped because the receiver failed: {exception.Message}");
        }
    }

    public static IResult PurgeHttpResult(PurgeResult result) => result.Status switch
    {
        PurgeStatus.Completed => Results.Ok(result),
        PurgeStatus.TimedOut => Results.Json(result, AppJsonContext.Default.PurgeResult, statusCode: StatusCodes.Status408RequestTimeout),
        PurgeStatus.Unauthorized => Results.Json(result, AppJsonContext.Default.PurgeResult, statusCode: StatusCodes.Status403Forbidden),
        PurgeStatus.SessionRequired => Results.Json(result, AppJsonContext.Default.PurgeResult, statusCode: StatusCodes.Status409Conflict),
        _ => Results.Json(result, AppJsonContext.Default.PurgeResult, statusCode: StatusCodes.Status502BadGateway)
    };

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
