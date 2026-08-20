using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

namespace ServiceBusEmulatorExplorer.Endpoints;

public static class SubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("topics/{topic}/subscriptions")
            .WithTags("Subscriptions");

        group.MapGet("/", ListSubscriptions)
            .WithName("ListSubscriptions")
            .WithSummary("List subscriptions on topic")
            .Produces<IReadOnlyList<SubscriptionInfo>>();

        group.MapPost("/", CreateSubscription)
            .WithName("CreateSubscription")
            .WithSummary("Create subscription")
            .Produces(StatusCodes.Status200OK);

        group.MapDelete("/{sub}", DeleteSubscription)
            .WithName("DeleteSubscription")
            .WithSummary("Delete subscription")
            .Produces(StatusCodes.Status200OK);

        group.MapGet("/{sub}/messages", PeekSubscriptionMessages)
            .WithName("PeekSubscriptionMessages")
            .WithSummary("Peek subscription messages")
            .Produces<PagedMessages>();

        group.MapPost("/{sub}/purge", PurgeSubscriptionMessages)
            .WithName("PurgeSubscriptionMessages")
            .WithSummary("Purge active subscription messages")
            .Produces<PurgeResult>()
            .Produces<PurgeResult>(StatusCodes.Status403Forbidden)
            .Produces<PurgeResult>(StatusCodes.Status408RequestTimeout)
            .Produces<PurgeResult>(StatusCodes.Status409Conflict)
            .Produces<PurgeResult>(StatusCodes.Status502BadGateway);

        return app;
    }

    private static async Task<IResult> ListSubscriptions(
        string topic,
        ServiceBusAdministrationClient client,
        ServiceBusEndpointCache endpointCache)
    {
        var subscriptionsRuntimeProperties = client.GetSubscriptionsRuntimePropertiesAsync(topic);
        if (subscriptionsRuntimeProperties is null)
        {
            return Results.NotFound();
        }

        var subscriptions = new List<SubscriptionInfo>();

        await foreach (var item in subscriptionsRuntimeProperties)
        {
            SubscriptionProperties? subProps = null;
            try
            {
                var subResponse = await client.GetSubscriptionAsync(topic, item.SubscriptionName);
                subProps = subResponse?.Value;
            }
            catch (Exception)
            {
                // Subscription properties may not be available on some emulator builds
            }

            var activeCountTask = Helpers.CountMessagesAsync(
                endpointCache, endpointCache.GetTopicReceiver(topic, item.SubscriptionName, new ServiceBusReceiverOptions { SubQueue = SubQueue.None }));
            var deadLetterCountTask = Helpers.CountMessagesAsync(
                endpointCache, endpointCache.GetTopicReceiver(topic, item.SubscriptionName, new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter }));
            await Task.WhenAll(activeCountTask, deadLetterCountTask);
            var activeCount = activeCountTask.Result;
            var deadLetterCount = deadLetterCountTask.Result;

            var subscriptionInfo = new SubscriptionInfo(
                item.SubscriptionName,
                EntityStatus.Active,
                activeCount.Count,
                deadLetterCount.Count,
                MaxDeliveryCount: subProps?.MaxDeliveryCount,
                LockDuration: subProps?.LockDuration.ToString(),
                DefaultTtl: subProps?.DefaultMessageTimeToLive.ToString(),
                CreatedAt: item.CreatedAt.UtcDateTime,
                ActiveMessageCountIsExact: activeCount.IsExact,
                DeadLetterMessageCountIsExact: deadLetterCount.IsExact
            );

            subscriptions.Add(subscriptionInfo);
        }

        return Results.Ok(subscriptions);
    }

    private static async Task<IResult> CreateSubscription(string topic, CreateSubscriptionRequest request,
        ServiceBusAdministrationClient client)
    {

        try
        {
            var currentSubscription = await client.GetSubscriptionAsync(topic, request.Name);

            if (currentSubscription is not null)
            {
                return Results.Problem(
                    $"Subscription with name '{request.Name}' already exists on topic '{topic}'.",
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Conflict");
            }
        }
        catch (ServiceBusException e) when(e.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            //ignore not found
        }

        await client.CreateSubscriptionAsync(new CreateSubscriptionOptions(topic, request.Name)
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
        });
        return Results.Ok();
    }

    private static async Task<IResult> DeleteSubscription(string topic, string sub,
        ServiceBusAdministrationClient client)
    {
        var currentSubscription = await client.GetSubscriptionAsync(topic, sub);

        if (currentSubscription is  null)
        {
            return Results.Problem(
                $"Subscription with name '{sub}' does not exist on topic '{topic}'.",
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found");
        }

        await client.DeleteSubscriptionAsync(topic, sub);
        return Results.Ok();
    }

    private static async Task<IResult> PeekSubscriptionMessages(
        string topic,
        string sub,
        CaseInsensitiveEnum<PeekMode> mode,
        CaseInsensitiveEnum<MessageState> state,
        ServiceBusEndpointCache endpointCache,
        int skip = 0,
        int take = 25)
    {
        var options = new ServiceBusReceiverOptions
        {
            SubQueue = state.Value switch
            {
                MessageState.Deadletter => SubQueue.DeadLetter,
                _ => SubQueue.None
            },
            ReceiveMode = ServiceBusReceiveMode.PeekLock
        };

        var receiver = endpointCache.GetTopicReceiver(topic, sub, options);

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

    private static async Task<IResult> PurgeSubscriptionMessages(
        string topic,
        string sub,
        ServiceBusAdministrationClient client,
        ServiceBusEndpointCache endpointCache,
        HttpContext httpContext)
    {
        try
        {
            var subscription = await client.GetSubscriptionAsync(topic, sub, httpContext.RequestAborted);
            if (subscription.Value.RequiresSession)
            {
                return Helpers.PurgeHttpResult(new PurgeResult(PurgeStatus.SessionRequired, 0,
                    "Purge is unavailable because this subscription requires sessions. Session-aware purge is not supported."));
            }
        }
        catch (UnauthorizedAccessException)
        {
            return Helpers.PurgeHttpResult(new PurgeResult(PurgeStatus.Unauthorized, 0,
                "The configured Service Bus credentials are not authorized to inspect this subscription."));
        }
        catch (RequestFailedException exception) when (exception.Status is 401 or 403)
        {
            return Helpers.PurgeHttpResult(new PurgeResult(PurgeStatus.Unauthorized, 0,
                "The configured Service Bus credentials are not authorized to inspect this subscription."));
        }
        catch (ServiceBusException exception) when (exception.InnerException is UnauthorizedAccessException)
        {
            return Helpers.PurgeHttpResult(new PurgeResult(PurgeStatus.Unauthorized, 0,
                "The configured Service Bus credentials are not authorized to inspect this subscription."));
        }
        catch (Exception)
        {
            // Some emulator builds do not expose entity properties. The receiver remains authoritative.
        }

        var receiver = endpointCache.GetTopicReceiver(topic, sub, new ServiceBusReceiverOptions
        {
            SubQueue = SubQueue.None,
            ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete,
        });

        var result = await Helpers.PurgeMessagesAsync(endpointCache, receiver, cancellationToken: httpContext.RequestAborted);
        return Helpers.PurgeHttpResult(result);
    }
}
