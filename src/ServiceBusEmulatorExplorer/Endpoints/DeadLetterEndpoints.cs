using System.Diagnostics;
using System.Collections.Concurrent;
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
            .Produces<ReplayDlqResult>(StatusCodes.Status200OK)
            .Produces<ReplayDlqResult>(StatusCodes.Status207MultiStatus);

        group.MapPost("/subscription/{topic}/{sub}/replay", ReplaySubscriptionDlq)
            .WithName("ReplaySubscriptionDlq")
            .WithSummary("Replay subscription DLQ messages")
            .Produces<ReplayDlqResult>(StatusCodes.Status200OK)
            .Produces<ReplayDlqResult>(StatusCodes.Status207MultiStatus);

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
        CancellationToken cancellationToken,
        Action<IReadOnlyList<ServiceBusReceivedMessage>>? onLocked = null)
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
            onLocked?.Invoke(fresh);
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

        cancellationToken.ThrowIfCancellationRequested();
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
        var completed = new ConcurrentDictionary<long, byte>();
        var replayed = 0;
        var outcomes = new List<ReplayMessageOutcome>();
        var renewalFailures = new ConcurrentDictionary<long, string>();
        string? operationError = null;
        IAsyncDisposable? operationLock = null;
        CancellationTokenSource? renewalCts = null;
        Task? renewalTask = null;
        var renewing = new ConcurrentDictionary<long, ServiceBusReceivedMessage>();

        try
        {
            operationLock = await cache.LockAsync(receiver, cts.Token);
            renewalCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            renewalTask = RenewLocksAsync(receiver, renewing, completed, renewalFailures, renewalCts.Token);
            (locked, notFound) = await LockDeadLetterMessagesAsync(receiver, wanted, cts.Token, messages =>
            {
                foreach (var message in messages)
                    renewing.TryAdd(message.SequenceNumber, message);
            });

            foreach (var message in locked)
            {
                if (wanted is not null && !wanted.Contains(message.MessageId)) continue;

                if (renewalFailures.TryGetValue(message.SequenceNumber, out var renewalError))
                {
                    outcomes.Add(new(message.MessageId, false, false, renewalError));
                    continue;
                }

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

                try
                {
                    await sender.SendMessageAsync(replay, cts.Token);
                }
                catch (Exception e) when (e is not OperationCanceledException || cts.IsCancellationRequested)
                {
                    Activity.Current?.AddException(e);
                    outcomes.Add(new(message.MessageId, false, false,
                        cts.IsCancellationRequested ? "Replay timed out before the message could be sent." : e.Message));
                    if (cts.IsCancellationRequested) break;
                    continue;
                }

                if (request.RemoveFromDlq)
                {
                    try
                    {
                        await receiver.CompleteMessageAsync(message, cts.Token);
                        completed.TryAdd(message.SequenceNumber, 0);
                        replayed++;
                        outcomes.Add(new(message.MessageId, true, true));
                    }
                    catch (Exception e)
                    {
                        Activity.Current?.AddException(e);
                        outcomes.Add(new(message.MessageId, true, false,
                            cts.IsCancellationRequested ? "Message was sent, but replay timed out before it could be removed from the DLQ." : e.Message));
                        if (cts.IsCancellationRequested) break;
                    }
                }
                else
                {
                    replayed++;
                    outcomes.Add(new(message.MessageId, true, false));
                }
            }
        }
        catch (OperationCanceledException)
        {
            operationError = "Replay timed out before all requested messages could be processed.";
            // Outcomes for work that had not started are added below, alongside not-found messages.
        }
        finally
        {
            if (renewalCts is not null)
            {
                await renewalCts.CancelAsync();
                if (renewalTask is not null)
                    try { await renewalTask; }
                    catch (OperationCanceledException) { }
                renewalCts.Dispose();
            }

            // Keep the canonical entity/subqueue lock until every unsettled broker lock is released.
            await ReleaseAsync(receiver, locked.Where(message => !completed.ContainsKey(message.SequenceNumber)));
            if (operationLock is not null)
                await operationLock.DisposeAsync();
            await sender.DisposeAsync();
        }

        if (wanted is null)
        {
            var reported = outcomes.Select(outcome => outcome.MessageId).ToHashSet(StringComparer.Ordinal);
            foreach (var message in locked.Where(message => !reported.Contains(message.MessageId)))
                outcomes.Add(new(message.MessageId, false, false, "Replay timed out before this message was processed."));
        }
        else
        {
            var reported = outcomes
                .GroupBy(outcome => outcome.MessageId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            outcomes = wanted.Select(messageId => reported.GetValueOrDefault(messageId) ??
                new ReplayMessageOutcome(messageId, false, false,
                    notFound.Contains(messageId)
                        ? "Message was not found in the DLQ."
                        : operationError ?? "Message was visible in the DLQ but could not be locked for replay."))
                .ToList();
        }

        var isPartial = operationError is not null || outcomes.Any(outcome => outcome.Error is not null);
        var result = new ReplayDlqResult(replayed, isPartial, outcomes, notFound.Count > 0 ? notFound.ToList() : null, operationError);
        return isPartial
            ? Results.Json(result, AppJsonContext.Default.ReplayDlqResult, statusCode: StatusCodes.Status207MultiStatus)
            : Results.Ok(result);
    }

    private static async Task RenewLocksAsync(
        ServiceBusReceiver receiver,
        ConcurrentDictionary<long, ServiceBusReceivedMessage> messages,
        ConcurrentDictionary<long, byte> completed,
        ConcurrentDictionary<long, string> failures,
        CancellationToken cancellationToken)
    {
        var renewalIntervals = new Dictionary<long, TimeSpan>();
        var nextRenewal = new Dictionary<long, DateTimeOffset>();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;

            foreach (var message in messages.Values.Where(message => !nextRenewal.ContainsKey(message.SequenceNumber)))
            {
                var interval = RenewalInterval(message.LockedUntil);
                renewalIntervals[message.SequenceNumber] = interval;
                nextRenewal[message.SequenceNumber] = now + interval;
            }

            foreach (var message in messages.Values.Where(message =>
                         nextRenewal.TryGetValue(message.SequenceNumber, out var due) && due <= now))
            {
                if (completed.ContainsKey(message.SequenceNumber))
                {
                    nextRenewal.Remove(message.SequenceNumber);
                    messages.TryRemove(message.SequenceNumber, out _);
                    continue;
                }

                try
                {
                    await receiver.RenewMessageLockAsync(message, cancellationToken);
                    nextRenewal[message.SequenceNumber] = DateTimeOffset.UtcNow + renewalIntervals[message.SequenceNumber];
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Activity.Current?.AddException(e);
                    failures.TryAdd(message.SequenceNumber, $"The DLQ message lock could not be renewed: {e.Message}");
                    nextRenewal.Remove(message.SequenceNumber);
                    messages.TryRemove(message.SequenceNumber, out _);
                }
            }

            var delay = nextRenewal.Count == 0
                ? TimeSpan.FromMilliseconds(25)
                : nextRenewal.Values.Min() - DateTimeOffset.UtcNow;
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(delay.TotalMilliseconds, 25, 1000)), cancellationToken);
        }
    }

    private static TimeSpan RenewalInterval(DateTimeOffset lockedUntil)
    {
        var halfRemainingMs = (lockedUntil - DateTimeOffset.UtcNow).TotalMilliseconds / 2;
        return TimeSpan.FromMilliseconds(Math.Clamp(halfRemainingMs, 25, 10000));
    }
}
