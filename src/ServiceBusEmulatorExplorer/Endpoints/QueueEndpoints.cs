using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.AspNetCore.Mvc;

namespace ServiceBusEmulatorExplorer.Endpoints;

public static class QueueEndpoints
{
    public static IEndpointRouteBuilder MapQueueEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("queues")
            .WithTags("Queues");

        group.MapGet("/", ListQueues)
            .WithName("ListQueues")
            .WithSummary("List queues")
            .Produces<IReadOnlyList<QueueInfo>>();

        group.MapPost("/", CreateQueue)
            .WithName("CreateQueue")
            .WithSummary("Create queue")
            .Produces(StatusCodes.Status200OK);

        group.MapDelete("/{name}", DeleteQueue)
            .WithName("DeleteQueue")
            .WithSummary("Delete queue")
            .Produces(StatusCodes.Status200OK);

        group.MapGet("/{name}/messages", PeekQueueMessages)
            .WithName("PeekQueueMessages")
            .WithSummary("Peek queue messages")
            .Produces<PagedMessages>();

        group.MapPost("/{name}/messages", SendQueueMessage)
            .WithName("SendQueueMessage")
            .WithSummary("Send message to queue")
            .Produces(StatusCodes.Status200OK);

        return app;
    }

    private static async Task<IResult> ListQueues([FromServices] ServiceBusAdministrationClient client)
    {
        var queuesRuntimeProperties = client.GetQueuesRuntimePropertiesAsync();

        if (queuesRuntimeProperties is null)
        {
            return Results.NotFound();
        }

        var queues = new List<QueueInfo>();

        await foreach (var item in queuesRuntimeProperties)
        {
            QueueProperties? queueProps = null;
            try
            {
                var queueResponse = await client.GetQueueAsync(item.Name);
                queueProps = queueResponse?.Value;
            }
            catch (Exception)
            {
                // Queue properties may not be available (e.g. emulator limitation)
            }

            var queueInfo = new QueueInfo(
                item.Name,
                EntityStatus.Active,
                item.ActiveMessageCount,
                item.DeadLetterMessageCount,
                item.ScheduledMessageCount,
                queueProps?.MaxDeliveryCount,
                queueProps?.LockDuration.ToString(),
                queueProps?.DefaultMessageTimeToLive.ToString(),
                item.CreatedAt);

            queues.Add(queueInfo);
        }

        return Results.Ok(queues);
    }

    private static async Task<IResult> CreateQueue(CreateQueueRequest request, ServiceBusAdministrationClient client)
    {
        var options = new CreateQueueOptions(request.Name)
        {
            DefaultMessageTimeToLive = request.DefaultTtl switch
            {
                _ when TimeSpan.TryParse(request.DefaultTtl, out var parsedTtl) => parsedTtl,
                _ => TimeSpan.FromDays(14)
            },
            LockDuration = request.LockDuration switch
            {
                _ when TimeSpan.TryParse(request.LockDuration, out var parsedLockDuration) => parsedLockDuration,
                _ => TimeSpan.FromMinutes(1)
            },
            MaxDeliveryCount = request.MaxDeliveryCount ?? 10
        };

        Response<QueueProperties>? response = await client.CreateQueueAsync(options);

        return response switch
        {
            { HasValue: true } => Results.Ok(response.Value.Name),
            _ => Results.Problem("Failed to create queue", statusCode: StatusCodes.Status500InternalServerError, title: "Internal Server Error")
        };
    }

    private static async Task<IResult> DeleteQueue(string name, ServiceBusAdministrationClient client)
    {
        var response = await client.DeleteQueueAsync(name);

        return Results.StatusCode(response.Status);
    }

    private static async Task<IResult> PeekQueueMessages(
        string name,
        [FromQuery] CaseInsensitiveEnum<PeekMode> mode,
        [FromQuery] CaseInsensitiveEnum<MessageState> state,
        ServiceBusEndpointCache endpointCache,
        int skip = 0,
        int take = 25)
    {
        var receiverOptions = new ServiceBusReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            SubQueue = state == MessageState.Deadletter ? SubQueue.DeadLetter : SubQueue.None
        };

        var receiver = endpointCache.GetReceiver(name, receiverOptions);

        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        IReadOnlyList<ServiceBusReceivedMessage>? messages = [];
        try
        {
            long fromSequenceNumber = 0;
            if (skip > 0)
            {
                var skipped = await receiver.PeekMessagesAsync(
                    maxMessages: skip, fromSequenceNumber: 0, cancellationToken: cancellationTokenSource.Token);
                if (skipped.Count > 0)
                {
                    fromSequenceNumber = skipped[^1].SequenceNumber + 1;
                }
            }

            messages = await receiver.PeekMessagesAsync(
                maxMessages: take,
                fromSequenceNumber: fromSequenceNumber, cancellationToken: cancellationTokenSource.Token);
        }
        catch (Exception)
        {
            // ignored
        }

        var messageInfos = messages.Select(message => new MessageInfo(
            message.MessageId,
            message.Body.ToString().Length <= 50 ? message.Body.ToString() : message.Body.ToString()[..50],
            message.Body.ToString(),
            message.EnqueuedTime.UtcDateTime,
            message.ExpiresAt.UtcDateTime,
            message.DeliveryCount,
            message.ContentType,
            message.SessionId,
            message.GetRawAmqpMessage().MessageAnnotations.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            message.ApplicationProperties.ToDictionary(kvp => kvp.Key, kvp => kvp.Value))).ToList();

        var pagedMessages = new PagedMessages(messageInfos, messageInfos.Count, messageInfos.Count == take);

        return Results.Ok(pagedMessages);
    }

    private static async Task<IResult> SendQueueMessage(string name, SendMessageRequest request,
        ServiceBusEndpointCache endpointCache)
    {
        var message = new ServiceBusMessage(request.Body)
        {
            ContentType = request.ContentType,
            MessageId = Guid.NewGuid().ToString(),
        };

        foreach (var requestUserProperty in request.UserProperties ?? [])
        {
            message.ApplicationProperties[requestUserProperty.Key] = requestUserProperty.Value.ToString();
        }

        var sender = endpointCache.GetSender(name);
        await sender.SendMessageAsync(message);

        return Results.Ok();
    }
}
