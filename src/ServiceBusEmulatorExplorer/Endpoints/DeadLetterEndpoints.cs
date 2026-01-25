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
}