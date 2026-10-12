using System.Net;
using System.Net.Http.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ServiceBusEmulatorExplorer.Tests;

[NotInParallel]
public class PurgeReceiverTests : TestBase
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task DestructiveDrainPreservesLaterMessagesAndUsesFreshReceivers(bool subscription, bool deadLetter)
    {
        var bus = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        var cache = Factory.Services.GetRequiredService<ServiceBusEndpointCache>();
        var name = $"post-drain-{subscription}-{deadLetter}";
        var entityPath = subscription ? $"{name}/Subscriptions/sub" : name;
        var route = deadLetter
            ? subscription ? $"/api/deadletter/subscription/{name}/sub/delete" : $"/api/deadletter/queue/{name}/delete"
            : subscription ? $"/api/topics/{name}/subscriptions/sub/purge" : $"/api/queues/{name}/purge";
        var options = new ServiceBusReceiverOptions { SubQueue = deadLetter ? SubQueue.DeadLetter : SubQueue.None };
        var peekReceiver = subscription ? cache.GetTopicReceiver(name, "sub", options) : cache.GetReceiver(name, options);
        using var http = Factory.CreateClient();

        // Start with an already-empty entity, then drain populated entities twice.
        for (var round = 0; round < 3; round++)
        {
            if (round > 0)
            {
                if (deadLetter) bus.AddDeadLetterMessage(entityPath, $"before-{round}", "before");
                else bus.AddActiveMessage(entityPath, $"before-{round}", "before");
            }

            var response = await http.PostAsync(route, null);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var removed = deadLetter
                ? (await response.Content.ReadFromJsonAsync<DeleteDlqResult>())!.Count
                : (await response.Content.ReadFromJsonAsync<PurgeResult>())!.RemovedCount;
            await Assert.That(removed).IsEqualTo(round == 0 ? 0 : 2);
            var drains = bus.CreatedReceivers.Where(receiver => receiver.EntityPath == entityPath && receiver.UsesReceiveAndDelete).ToList();
            await Assert.That(drains).Count().IsEqualTo(round + 1);
            await Assert.That(drains.All(receiver => receiver.IsDisposed)).IsTrue();
            await Assert.That(peekReceiver.IsClosed).IsFalse();

            if (deadLetter) bus.AddDeadLetterMessage(entityPath, $"after-{round}", "after");
            // Subscription sends are seeded directly: this fake does not fan topic sends out to subscriptions.
            else if (subscription) bus.AddActiveMessage(entityPath, $"after-{round}", "after");
            else
            {
                var send = await http.PostAsJsonAsync($"/api/queues/{entityPath}/messages", new { body = "after" });
                await Assert.That(send.StatusCode).IsEqualTo(HttpStatusCode.OK);
            }

            await using var operationLock = await cache.LockAsync(peekReceiver);
            var messages = await peekReceiver.PeekMessagesAsync(100, fromSequenceNumber: 0);
            await Assert.That(messages).Count().IsEqualTo(1);
            await Assert.That(messages[0].Body.ToString()).IsEqualTo("after");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FailedOrTimedOutDlqDrainClosesReceiverAndPreservesLaterMessages(bool timeout)
    {
        var bus = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        var options = Factory.Services.GetRequiredService<IOptions<DlqOperationOptions>>().Value;
        var originalTimeout = options.OperationTimeout;
        var name = $"dlq-drain-error-{timeout}";
        bus.AddDeadLetterMessage(name, "one", "one");
        bus.AddDeadLetterMessage(name, "two", "two");
        bus.DeadLetterReceiveBatchSize = 1;
        if (timeout)
        {
            options.OperationTimeout = TimeSpan.FromMilliseconds(100);
            bus.DeadLetterReceiveDelayAfterFirstBatch = TimeSpan.FromSeconds(5);
        }
        else bus.FailDeadLetterReceiveAfterCalls = 1;
        try
        {
            var response = await Factory.CreateClient().PostAsync($"/api/deadletter/queue/{name}/delete", null);
            await Assert.That((int)response.StatusCode).IsEqualTo(207);
            var result = await response.Content.ReadFromJsonAsync<DeleteDlqResult>();
            await Assert.That(result!.Count).IsEqualTo(1);
            await Assert.That(result.Status).IsEqualTo(timeout ? DlqDeleteStatus.TimedOut : DlqDeleteStatus.Partial);
            await Assert.That(bus.CreatedReceivers.Last().IsDisposed).IsTrue();
            bus.AddDeadLetterMessage(name, "after", "preserved");
            await Assert.That(bus.GetDeadLetterMessages(name)).Count().IsEqualTo(2);
        }
        finally
        {
            options.OperationTimeout = originalTimeout;
            bus.DeadLetterReceiveDelayAfterFirstBatch = null;
            bus.FailDeadLetterReceiveAfterCalls = null;
            bus.DeadLetterReceiveBatchSize = null;
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReceiverLifecycleFailureReleasesEntityLock(bool creationFailure)
    {
        await using var bus = new TestServiceBusClient();
        await using var cache = new ServiceBusEndpointCache(bus);
        var cached = cache.GetReceiver("lifecycle-failure", new ServiceBusReceiverOptions());
        bus.FailReceiverCreation = creationFailure;
        bus.FailReceiveAndDeleteDispose = !creationFailure;
        var result = await Helpers.PurgeMessagesAsync(token => cache.CreateReceiverScopeAsync("lifecycle-failure",
            new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete }, token));
        await Assert.That(result.Status).IsEqualTo(PurgeStatus.Failed);
        await using var operationLock = await cache.LockAsync(cached).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task FakeModelsOutstandingReceiveAndDeleteCredit()
    {
        await using var bus = new TestServiceBusClient();
        var receiver = bus.CreateReceiver("credit", new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });
        await receiver.ReceiveMessagesAsync(100, TimeSpan.FromSeconds(1));
        bus.AddActiveMessage("credit", "lost", "lost");
        await Assert.That(bus.GetSentMessages("credit")).IsEmpty();
        await receiver.DisposeAsync();
        bus.AddActiveMessage("credit", "retained", "retained");
        await Assert.That(bus.GetSentMessages("credit")).Count().IsEqualTo(1);
    }

    [Test]
    public async Task DrainHoldsSharedSubqueueLockUntilReceiverIsDisposed()
    {
        await using var bus = new TestServiceBusClient { BlockReceiveAndDeleteDispose = true };
        await using var cache = new ServiceBusEndpointCache(bus);
        var cached = cache.GetReceiver("locked", new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
        var scope = await cache.CreateReceiverScopeAsync("locked", new ServiceBusReceiverOptions
        {
            SubQueue = SubQueue.DeadLetter,
            ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete
        });
        var disposing = scope.DisposeAsync().AsTask();
        await bus.ReceiverDisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var peekLock = cache.LockAsync(cached);
        var nextDrain = cache.CreateReceiverScopeAsync("locked", new ServiceBusReceiverOptions
        {
            SubQueue = SubQueue.DeadLetter,
            ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete
        });
        try
        {
            await Assert.That(peekLock.IsCompleted).IsFalse();
            await Assert.That(nextDrain.IsCompleted).IsFalse();
            // The active subqueue is independent from its DLQ.
            await using var active = await cache.CreateReceiverScopeAsync("locked", new ServiceBusReceiverOptions());
        }
        finally
        {
            bus.AllowReceiverDispose.TrySetResult();
            await disposing;
            await (await peekLock.WaitAsync(TimeSpan.FromSeconds(5))).DisposeAsync();
            await (await nextDrain.WaitAsync(TimeSpan.FromSeconds(5))).DisposeAsync();
        }
        await Assert.That(scope.Receiver.IsClosed).IsTrue();
        await Assert.That(cached.IsClosed).IsFalse();
    }

    [Test]
    public async Task CancelledDrainWaitingForLockDoesNotCreateReceiver()
    {
        await using var bus = new TestServiceBusClient();
        await using var cache = new ServiceBusEndpointCache(bus);
        var cached = cache.GetReceiver("cancelled", new ServiceBusReceiverOptions());
        await using var operationLock = await cache.LockAsync(cached);
        using var cts = new CancellationTokenSource();
        var purge = Helpers.PurgeMessagesAsync(token => cache.CreateReceiverScopeAsync("cancelled",
            new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete }, token), cancellationToken: cts.Token);
        cts.Cancel();
        var result = await purge.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(result.Status).IsEqualTo(PurgeStatus.Failed);
        await Assert.That(bus.CreatedReceivers).Count().IsEqualTo(1);
    }
}
