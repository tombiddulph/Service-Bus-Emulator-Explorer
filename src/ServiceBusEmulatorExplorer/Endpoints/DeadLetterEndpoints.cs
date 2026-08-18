using System.Diagnostics;
using Azure.Messaging.ServiceBus;

namespace ServiceBusEmulatorExplorer.Endpoints;

public static class DeadLetterEndpoints
{
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

    private static async Task<IResult> BulkDeleteQueueDlq(string name, ServiceBusEndpointCache cache,
        BulkDlqDeleteRequest? request = null)
    {
        var receiver = cache.GetReceiver(
            queue: name,
            receiverOptions: new()
            {
                SubQueue = SubQueue.DeadLetter,
                ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete,
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            await receiver.ReceiveMessagesAsync(cts.Token).ToListAsync(cts.Token);
        }
        catch (Exception e)
        {
            Activity.Current?.AddException(e);
        }

        return Results.Ok();
    }

    private static async Task<IResult> BulkDeleteSubscriptionDlq(
        string topic,
        string sub,
        ServiceBusEndpointCache cache,
        BulkDlqDeleteRequest? request = null)
    {
       var receiver = cache.GetTopicReceiver(
            topic: topic,
            subscription: sub,
            receiverOptions: new()
            {
                SubQueue = SubQueue.DeadLetter,
                ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete,
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            await receiver.ReceiveMessagesAsync(cts.Token).ToListAsync(cts.Token);
        }
        catch (Exception e)
        {
            Activity.Current?.AddException(e);
        }

        return Results.Ok();
    }

    private static Task<IResult> ReplayQueueDlq(string name, ReplayDlqRequest request, ServiceBusEndpointCache cache, ServiceBusClient client) =>
        ReplayDlq(cache.GetReceiver(name, new() { SubQueue = SubQueue.DeadLetter, ReceiveMode = ServiceBusReceiveMode.PeekLock }),
            client.CreateSender(name), request);

    private static Task<IResult> ReplaySubscriptionDlq(string topic, string sub, ReplayDlqRequest request,
        ServiceBusEndpointCache cache, ServiceBusClient client) =>
        ReplayDlq(cache.GetTopicReceiver(topic, sub, new() { SubQueue = SubQueue.DeadLetter, ReceiveMode = ServiceBusReceiveMode.PeekLock }),
            client.CreateSender(topic), request);

    private static async Task<IResult> ReplayDlq(ServiceBusReceiver receiver, ServiceBusSender sender, ReplayDlqRequest request)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var wanted = request.MessageIds is { Count: > 0 } ? request.MessageIds.ToHashSet(StringComparer.Ordinal) : null;
        var processed = new HashSet<string>(StringComparer.Ordinal);
        var replayed = 0;

        try
        {
            while (wanted is null || wanted.Count > 0)
            {
                var messages = await receiver.ReceiveMessagesAsync(100, cancellationToken: cts.Token);
                if (messages.Count == 0) break;
                var newMessages = messages.Where(message => processed.Add(message.MessageId)).ToList();
                if (wanted is null && newMessages.Count == 0) break;

                foreach (var message in messages)
                {
                    if (!newMessages.Contains(message))
                    {
                        await receiver.AbandonMessageAsync(message, cancellationToken: cts.Token);
                        continue;
                    }

                    if (wanted is not null && !wanted.Contains(message.MessageId))
                    {
                        await receiver.AbandonMessageAsync(message, cancellationToken: cts.Token);
                        continue;
                    }

                    var replay = request.Body is null
                        ? new ServiceBusMessage(message.Body)
                        : new ServiceBusMessage(request.Body)
                    {
                        ContentType = request.ContentType ?? message.ContentType,
                        MessageId = message.MessageId
                    };
                    var properties = request.UserProperties ?? message.ApplicationProperties;
                    foreach (var property in properties)
                        replay.ApplicationProperties[property.Key] = property.Value;

                    await sender.SendMessageAsync(replay, cts.Token);
                    if (request.RemoveFromDlq)
                        await receiver.CompleteMessageAsync(message, cts.Token);
                    else
                        await receiver.AbandonMessageAsync(message, cancellationToken: cts.Token);
                    wanted?.Remove(message.MessageId);
                    replayed++;
                }

                if (wanted is null && request.RemoveFromDlq && messages.Count < 100) break;
            }
        }
        finally
        {
            await sender.DisposeAsync();
        }

        return Results.Ok(new { replayed });
    }
}