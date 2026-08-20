using System.Diagnostics;
using Azure.Messaging.ServiceBus;

namespace ServiceBusEmulatorExplorer.Endpoints;

public static class DeadLetterEndpoints
{
    private const string ReplayedFromPropertyName = "ReplayedFromMessageId";

    public static IEndpointRouteBuilder MapDeadLetterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("deadletter")
            .WithTags("Dead Letter");

        group.MapPost("/queue/{name}/delete", BulkDeleteQueueDlq)
            .WithName("BulkDeleteQueueDlq")
            .WithSummary("Bulk delete queue DLQ")
            .Produces(StatusCodes.Status200OK);

        group.MapPost("/subscription/{topic}/{sub}/delete", BulkDeleteSubscriptionDlq)
            .WithName("BulkDeleteSubscriptionDlq")
            .WithSummary("Bulk delete subscription DLQ")
            .Produces(StatusCodes.Status200OK);

        group.MapPost("/queue/{name}/replay", ReplayQueueDlq)
            .WithName("ReplayQueueDlq")
            .WithSummary("Replay queue DLQ messages")
            .Produces(StatusCodes.Status200OK);

        group.MapPost("/subscription/{topic}/{sub}/replay", ReplaySubscriptionDlq)
            .WithName("ReplaySubscriptionDlq")
            .WithSummary("Replay subscription DLQ messages")
            .Produces(StatusCodes.Status200OK);

        return app;
    }

    private static Task<IResult> BulkDeleteQueueDlq(string name, ServiceBusEndpointCache cache,
        BulkDlqDeleteRequest? request = null) =>
        BulkDeleteDlq(cache, options => cache.GetReceiver(name, options), request);

    private static Task<IResult> BulkDeleteSubscriptionDlq(
        string topic,
        string sub,
        ServiceBusEndpointCache cache,
        BulkDlqDeleteRequest? request = null) =>
        BulkDeleteDlq(cache, options => cache.GetTopicReceiver(topic, sub, options), request);

    private static async Task<IResult> BulkDeleteDlq(
        ServiceBusEndpointCache cache,
        Func<ServiceBusReceiverOptions, ServiceBusReceiver> receiverFactory,
        BulkDlqDeleteRequest? request)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        if (request?.MessageIds is not { Count: > 0 } requestedIds)
        {
            var purgeReceiver = receiverFactory(new()
            {
                SubQueue = SubQueue.DeadLetter,
                ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete,
            });

            try
            {
                await using var _ = await cache.LockAsync(purgeReceiver, cts.Token);
                await purgeReceiver.ReceiveMessagesAsync(cts.Token).ToListAsync(cts.Token);
            }
            catch (Exception e)
            {
                Activity.Current?.AddException(e);
            }

            return Results.Ok();
        }

        var receiver = receiverFactory(new()
        {
            SubQueue = SubQueue.DeadLetter,
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
        });

        var wanted = requestedIds.ToHashSet(StringComparer.Ordinal);
        var locked = new List<ServiceBusReceivedMessage>();
        var notFound = new HashSet<string>();
        var completed = new HashSet<long>();
        var deleted = 0;

        try
        {
            await using var _ = await cache.LockAsync(receiver, cts.Token);

            (locked, notFound) = await LockDeadLetterMessagesAsync(receiver, wanted, cts.Token);

            foreach (var message in locked.Where(message => wanted.Contains(message.MessageId)))
            {
                await receiver.CompleteMessageAsync(message, cts.Token);
                completed.Add(message.SequenceNumber);
                deleted++;
            }
        }
        catch (Exception e)
        {
            Activity.Current?.AddException(e);
        }
        finally
        {
            await ReleaseAsync(receiver, locked.Where(message => !completed.Contains(message.SequenceNumber)));
        }

        return Results.Ok(new CountResult(deleted, notFound.Count > 0 ? notFound.ToList() : null));
    }

    // Locks the smallest prefix of the dead letter queue that still contains every requested message.
    // Receiving is what ticks DeliveryCount, so peek (which does not) to find that boundary first.
    private static async Task<(List<ServiceBusReceivedMessage> Locked, HashSet<string> NotFound)> LockDeadLetterMessagesAsync(
        ServiceBusReceiver receiver,
        HashSet<string>? wanted,
        CancellationToken cancellationToken)
    {
        var (limit, notFound) = await CountMessagesToLockAsync(receiver, wanted, cancellationToken);

        var locked = new List<ServiceBusReceivedMessage>();
        var seen = new HashSet<long>();

        while (locked.Count < limit && !cancellationToken.IsCancellationRequested)
        {
            var batch = await receiver.ReceiveMessagesAsync(
                Math.Min(100, limit - locked.Count), TimeSpan.FromSeconds(2), cancellationToken);
            if (batch.Count == 0) break;

            var fresh = batch.Where(message => seen.Add(message.SequenceNumber)).ToList();
            if (fresh.Count == 0) break;

            locked.AddRange(fresh);
        }

        return (locked, notFound);
    }

    // Returns how many messages from the front of the DLQ must be received to cover every match,
    // plus any requested IDs never seen while scanning the whole queue.
    private static async Task<(int Needed, HashSet<string> NotFound)> CountMessagesToLockAsync(
        ServiceBusReceiver receiver,
        HashSet<string>? wanted,
        CancellationToken cancellationToken)
    {
        var remaining = wanted is null ? null : new HashSet<string>(wanted, StringComparer.Ordinal);
        long fromSequenceNumber = 0;
        var scanned = 0;
        var needed = 0;

        while ((remaining is null || remaining.Count > 0) && !cancellationToken.IsCancellationRequested)
        {
            var batch = await receiver.PeekMessagesAsync(100, fromSequenceNumber, cancellationToken);
            if (batch.Count == 0) break;

            foreach (var message in batch)
            {
                scanned++;
                if (remaining is null || remaining.Remove(message.MessageId)) needed = scanned;
            }

            fromSequenceNumber = batch[^1].SequenceNumber + 1;
        }

        return (needed, remaining ?? []);
    }

    private static async Task ReleaseAsync(ServiceBusReceiver receiver, IEnumerable<ServiceBusReceivedMessage> messages)
    {
        foreach (var message in messages)
        {
            try
            {
                await receiver.AbandonMessageAsync(message);
            }
            catch (Exception e)
            {
                Activity.Current?.AddException(e);
            }
        }
    }

    private static Task<IResult> ReplayQueueDlq(string name, ReplayDlqRequest request, ServiceBusEndpointCache cache, ServiceBusClient client) =>
        ReplayDlq(cache, cache.GetReceiver(name, new() { SubQueue = SubQueue.DeadLetter, ReceiveMode = ServiceBusReceiveMode.PeekLock }),
            client.CreateSender(name), request);

    private static Task<IResult> ReplaySubscriptionDlq(string topic, string sub, ReplayDlqRequest request,
        ServiceBusEndpointCache cache, ServiceBusClient client) =>
        ReplayDlq(cache, cache.GetTopicReceiver(topic, sub, new() { SubQueue = SubQueue.DeadLetter, ReceiveMode = ServiceBusReceiveMode.PeekLock }),
            client.CreateSender(topic), request);

    private static async Task<IResult> ReplayDlq(ServiceBusEndpointCache cache, ServiceBusReceiver receiver, ServiceBusSender sender,
        ReplayDlqRequest request)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var wanted = request.MessageIds is { Count: > 0 } ? request.MessageIds.ToHashSet(StringComparer.Ordinal) : null;

        // Reject unsupported JSON property shapes before locking any messages.
        Dictionary<string, object>? convertedUserProperties = null;
        if (request.UserProperties is { Count: > 0 })
        {
            convertedUserProperties = new Dictionary<string, object>();
            foreach (var (key, element) in request.UserProperties)
            {
                if (!Helpers.TryConvertApplicationProperty(element, out var value))
                    return Results.Problem($"Unsupported value for user property '{key}'.", statusCode: StatusCodes.Status400BadRequest, title: "Bad Request");
                convertedUserProperties[key] = value!;
            }
        }

        var locked = new List<ServiceBusReceivedMessage>();
        var notFound = new HashSet<string>();
        var completed = new HashSet<long>();
        var replayed = 0;

        try
        {
            await using var _ = await cache.LockAsync(receiver, cts.Token);

            (locked, notFound) = await LockDeadLetterMessagesAsync(receiver, wanted, cts.Token);

            foreach (var message in locked)
            {
                if (wanted is not null && !wanted.Contains(message.MessageId)) continue;

                // Copy every standard property (Subject, CorrelationId, SessionId, ...) so subscription
                // filters evaluate the replayed message the same way they did the original.
                var replay = new ServiceBusMessage(message)
                {
                    MessageId = Guid.NewGuid().ToString(),
                };
                replay.ApplicationProperties[ReplayedFromPropertyName] = message.MessageId;

                if (request.Body is not null)
                    replay.Body = new BinaryData(request.Body);

                if (request.ContentType is not null)
                    replay.ContentType = request.ContentType;

                if (convertedUserProperties is not null)
                {
                    replay.ApplicationProperties.Clear();
                    replay.ApplicationProperties[ReplayedFromPropertyName] = message.MessageId;
                    foreach (var (key, value) in convertedUserProperties)
                        replay.ApplicationProperties[key] = value;
                }

                await sender.SendMessageAsync(replay, cts.Token);

                if (request.RemoveFromDlq)
                {
                    await receiver.CompleteMessageAsync(message, cts.Token);
                    completed.Add(message.SequenceNumber);
                    replayed++;
                }
                else
                {
                    replayed++;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timed out mid-replay: report what actually completed instead of a false success.
            return Results.Json(
                new CountResult(replayed, notFound.Count > 0 ? notFound.ToList() : null),
                AppJsonContext.Default.CountResult,
                statusCode: StatusCodes.Status207MultiStatus);
        }
        finally
        {
            await ReleaseAsync(receiver, locked.Where(message => !completed.Contains(message.SequenceNumber)));
            await sender.DisposeAsync();
        }

        return Results.Ok(new CountResult(replayed, notFound.Count > 0 ? notFound.ToList() : null));
    }
}