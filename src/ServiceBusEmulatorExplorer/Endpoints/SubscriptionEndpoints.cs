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

        return app;
    }

    private static async Task<IResult> ListSubscriptions(string topic, ServiceBusAdministrationClient client)
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
                // Subscription properties may not be available (e.g. emulator limitation or entity not found)
            }

            // Prefer individual runtime properties for accurate counts — the batch call may return 0 on the emulator
            var activeCount = item.ActiveMessageCount;
            var dlqCount = item.DeadLetterMessageCount;
            try
            {
                var runtimeResponse = await client.GetSubscriptionRuntimePropertiesAsync(topic, item.SubscriptionName);
                if (runtimeResponse?.Value is { } runtimeProps)
                {
                    activeCount = runtimeProps.ActiveMessageCount;
                    dlqCount = runtimeProps.DeadLetterMessageCount;
                }
            }
            catch (Exception)
            {
                // Fall back to batch runtime properties
            }

            var subscriptionInfo = new SubscriptionInfo(
                item.SubscriptionName,
                EntityStatus.Active,
                activeCount,
                dlqCount,
                MaxDeliveryCount: subProps?.MaxDeliveryCount,
                LockDuration: subProps?.LockDuration.ToString(),
                DefaultTtl: subProps?.DefaultMessageTimeToLive.ToString(),
                CreatedAt: item.CreatedAt.UtcDateTime
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
        ServiceBusAdministrationClient adminClient,
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

        // Try to get accurate total count from subscription runtime properties.
        // The emulator may return 0 from GetSubscriptionsRuntimePropertiesAsync, so we query individually here.
        int? runtimeTotal = null;
        try
        {
            var runtimeProps = await adminClient.GetSubscriptionRuntimePropertiesAsync(topic, sub);
            if (runtimeProps?.Value is { } props)
            {
                runtimeTotal = (int)(state.Value == MessageState.Deadletter
                    ? props.DeadLetterMessageCount
                    : props.ActiveMessageCount);
            }
        }
        catch (Exception)
        {
            // Runtime properties unavailable — fall back to peeked count
        }

        var total = runtimeTotal > 0 ? runtimeTotal.Value : messageInfos.Count;
        var hasMore = runtimeTotal > 0
            ? skip + messageInfos.Count < runtimeTotal.Value
            : messageInfos.Count == take;

        var pagedMessages = new PagedMessages(messageInfos, total, hasMore);

        return Results.Ok(pagedMessages);
    }
}
