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

        group.MapPost("/{name}/purge", PurgeQueueMessages)
            .WithName("PurgeQueueMessages")
            .WithSummary("Purge active queue messages")
            .Produces<PurgeResult>()
            .Produces<PurgeResult>(StatusCodes.Status403Forbidden)
            .Produces<PurgeResult>(StatusCodes.Status408RequestTimeout)
            .Produces<PurgeResult>(StatusCodes.Status409Conflict)
            .Produces<PurgeResult>(StatusCodes.Status502BadGateway);

        return app;
    }

    private static async Task<IResult> ListQueues(
        [FromServices] ServiceBusAdministrationClient client,
        [FromServices] ServiceBusEndpointCache endpointCache)
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

            var activeCountTask = Helpers.CountMessagesAsync(
                endpointCache, endpointCache.GetReceiver(item.Name, new ServiceBusReceiverOptions { SubQueue = SubQueue.None }));
            var deadLetterCountTask = Helpers.CountMessagesAsync(
                endpointCache, endpointCache.GetReceiver(item.Name, new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter }));
            await Task.WhenAll(activeCountTask, deadLetterCountTask);
            var activeCount = activeCountTask.Result;
            var deadLetterCount = deadLetterCountTask.Result;

            var queueInfo = new QueueInfo(
                item.Name,
                EntityStatus.Active,
                activeCount.Count,
                deadLetterCount.Count,
                item.ScheduledMessageCount,
                queueProps?.MaxDeliveryCount,
                queueProps?.LockDuration.ToString(),
                queueProps?.DefaultMessageTimeToLive.ToString(),
                item.CreatedAt,
                ActiveMessageCountIsExact: activeCount.IsExact,
                DeadLetterMessageCountIsExact: deadLetterCount.IsExact);

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
            await using var _ = await endpointCache.LockAsync(receiver, cancellationTokenSource.Token);

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
            SessionId = request.SessionId,
        };

        foreach (var (key, element) in request.UserProperties ?? [])
        {
            if (!Helpers.TryConvertApplicationProperty(element, out var value))
                return Results.Problem($"Unsupported value for user property '{key}'.", statusCode: StatusCodes.Status400BadRequest, title: "Bad Request");
            message.ApplicationProperties[key] = value;
        }

        var sender = endpointCache.GetSender(name);
        await sender.SendMessageAsync(message);

        return Results.Ok();
    }

    private static async Task<IResult> PurgeQueueMessages(
        string name,
        ServiceBusAdministrationClient client,
        ServiceBusEndpointCache endpointCache,
        HttpContext httpContext)
    {
        try
        {
            var queue = await client.GetQueueAsync(name, httpContext.RequestAborted);
            if (queue.Value.RequiresSession)
            {
                return Helpers.PurgeHttpResult(new PurgeResult(PurgeStatus.SessionRequired, 0,
                    "Purge is unavailable because this queue requires sessions. Session-aware purge is not supported."));
            }
        }
        catch (UnauthorizedAccessException)
        {
            return Helpers.PurgeHttpResult(new PurgeResult(PurgeStatus.Unauthorized, 0,
                "The configured Service Bus credentials are not authorized to inspect this queue."));
        }
        catch (RequestFailedException exception) when (exception.Status is 401 or 403)
        {
            return Helpers.PurgeHttpResult(new PurgeResult(PurgeStatus.Unauthorized, 0,
                "The configured Service Bus credentials are not authorized to inspect this queue."));
        }
        catch (ServiceBusException exception) when (exception.InnerException is UnauthorizedAccessException)
        {
            return Helpers.PurgeHttpResult(new PurgeResult(PurgeStatus.Unauthorized, 0,
                "The configured Service Bus credentials are not authorized to inspect this queue."));
        }
        catch (Exception)
        {
            // Some emulator builds do not expose entity properties. The receiver remains authoritative.
        }

        var receiver = endpointCache.GetReceiver(name, new ServiceBusReceiverOptions
        {
            SubQueue = SubQueue.None,
            ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete,
        });

        var result = await Helpers.PurgeMessagesAsync(endpointCache, receiver, cancellationToken: httpContext.RequestAborted);
        return Helpers.PurgeHttpResult(result);
    }
}
