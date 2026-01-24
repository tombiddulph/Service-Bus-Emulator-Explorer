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
            var subscription = await client.GetSubscriptionRuntimePropertiesAsync(topic, item.SubscriptionName)!;

            if (subscription is null)
            {
                continue;
            }

            var subscriptionInfo = new SubscriptionInfo(
                item.SubscriptionName,
                EntityStatus.Active,
                subscription.Value.ActiveMessageCount,
                item.DeadLetterMessageCount
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

        await client.CreateSubscriptionAsync(new CreateSubscriptionOptions(topic, request.Name));
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
            messages = await receiver.PeekMessagesAsync(
                maxMessages: take,
                fromSequenceNumber: long.MinValue + skip, cancellationToken: cancellationTokenSource.Token);
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

        var pagedMessages = new PagedMessages(messageInfos, messageInfos.Count, messageInfos.Count != 0);

        return Results.Ok(pagedMessages);
    }
}
