using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

namespace ServiceBusEmulatorExplorer.Endpoints;

public static class TopicEndpoints
{
    public static IEndpointRouteBuilder MapTopicEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("topics")
            .WithTags("Topics");

        group.MapGet("/", ListTopics)
            .WithName("ListTopics")
            .WithSummary("List topics")
            .Produces<IReadOnlyList<TopicInfo>>();

        group.MapPost("/", CreateTopic)
            .WithName("CreateTopic")
            .WithSummary("Create topic")
            .Produces(StatusCodes.Status201Created);

        group.MapDelete("/{name}", DeleteTopic)
            .WithName("DeleteTopic")
            .WithSummary("Delete topic")
            .Produces(StatusCodes.Status200OK);

        group.MapPost("/{topic}/messages", SendTopicMessage)
            .WithName("SendTopicMessage")
            .WithSummary("Send message to topic")
            .Produces(StatusCodes.Status200OK);

        return app;
    }

    private static async Task<IResult> ListTopics(ServiceBusAdministrationClient client, ServiceBusEndpointCache endpointCache)
    {
        var topicsRuntimeProperties = client.GetTopicsRuntimePropertiesAsync();
        if (topicsRuntimeProperties is null)
        {
            return Results.NotFound();
        }

        var topics = new List<TopicInfo>();

        await foreach (var item in topicsRuntimeProperties)
        {
            var subscriptionNames = new List<string>();
            await foreach (var subscription in client.GetSubscriptionsRuntimePropertiesAsync(item.Name))
            {
                subscriptionNames.Add(subscription.SubscriptionName);
            }

            var countResults = await Task.WhenAll(subscriptionNames.Select(async subscriptionName =>
            {
                var active = await Helpers.CountMessagesAsync(
                    endpointCache, endpointCache.GetTopicReceiver(item.Name, subscriptionName, new() { SubQueue = SubQueue.None }));
                var deadLetter = await Helpers.CountMessagesAsync(
                    endpointCache, endpointCache.GetTopicReceiver(item.Name, subscriptionName, new() { SubQueue = SubQueue.DeadLetter }));
                return (Active: active, DeadLetter: deadLetter);
            }));

            long activeCount = 0;
            long deadLetterCount = 0;
            var activeIsExact = true;
            var deadLetterIsExact = true;
            foreach (var (active, deadLetter) in countResults)
            {
                activeCount += active.Count;
                deadLetterCount += deadLetter.Count;
                activeIsExact &= active.IsExact;
                deadLetterIsExact &= deadLetter.IsExact;
            }

            var topicInfo = new TopicInfo(
                item.Name,
                EntityStatus.Active,
                checked((int)Math.Min(activeCount, int.MaxValue)),
                checked((int)Math.Min(deadLetterCount, int.MaxValue)),
                ActiveMessageCountIsExact: activeIsExact,
                DeadLetterMessageCountIsExact: deadLetterIsExact
            );

            topics.Add(topicInfo);
        }

        return Results.Ok(topics);
    }

    private static async Task<IResult> CreateTopic(CreateTopicRequest request, ServiceBusAdministrationClient client) =>
        await client.CreateTopicAsync(new CreateTopicOptions(request.Name)) switch
        {
            { HasValue: true } => Results.Ok(),
            _ => Results.BadRequest()
        };

    private static async Task<IResult> DeleteTopic(string name, ServiceBusAdministrationClient client)
    {
        var exists = await client.GetTopicAsync(name);
        if (exists.Value is null)
        {
            return Results.Problem("Topic does not exist", statusCode: StatusCodes.Status400BadRequest, title: "Bad Request");
        }

        return (await client.DeleteTopicAsync(name)) switch
        {
            { IsError: false } => Results.Ok(),
            _ => Results.Problem("Failed to delete topic", statusCode: StatusCodes.Status500InternalServerError, title: "Internal Server Error")
        };
    }

    private static async Task<IResult> SendTopicMessage(string topic, SendMessageRequest request,
        ServiceBusAdministrationClient adminClient, ServiceBusClient client)
    {
        var exists = await adminClient.GetTopicAsync(topic);
        if (exists.Value is null)
        {
            return Results.Problem("Topic does not exist", statusCode: StatusCodes.Status400BadRequest, title: "Bad Request");
        }

        var message = new ServiceBusMessage(request.Body)
        {
            ContentType = request.ContentType,
            SessionId = request.SessionId,
        };

        foreach (var (key, element) in request.UserProperties ?? [])
        {
            if (!Helpers.TryConvertApplicationProperty(element, out var value))
                return Results.Problem($"Unsupported value for user property '{key}'.", statusCode: StatusCodes.Status400BadRequest, title: "Bad Request");
            message.ApplicationProperties[key] = value;
        }

        await using var sender = client.CreateSender(topic);
        await sender.SendMessageAsync(message);
        return Results.Ok();
    }
}
