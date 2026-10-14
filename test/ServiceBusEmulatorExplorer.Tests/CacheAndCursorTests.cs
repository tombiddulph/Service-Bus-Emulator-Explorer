using System.Net.Http.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;

namespace ServiceBusEmulatorExplorer.Tests;

[NotInParallel]
public class CacheAndCursorTests : TestBase
{
    [Test]
    public async Task ConcurrentCountsShareOneScanAndInvalidationRefreshes()
    {
        var broker = new TestServiceBusClient();
        await using var cache = new ServiceBusEndpointCache(broker);
        var receiver = cache.GetReceiver("cache-test", new());
        broker.AddActiveMessage("cache-test", "one", "body");
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => cache.GetCountAsync(receiver)));
        await Assert.That(results.All(result => result.Count == 1)).IsTrue();
        await Assert.That(broker.PeekCallCount).IsEqualTo(2);
        broker.AddActiveMessage("cache-test", "two", "body");
        cache.InvalidateCounts();
        await Assert.That((await cache.GetCountAsync(receiver)).Count).IsEqualTo(2);
    }

    [Test]
    public async Task RemovingEntityClosesReceiversAndAllowsFreshReceiver()
    {
        var broker = new TestServiceBusClient();
        await using var cache = new ServiceBusEndpointCache(broker);
        var receiver = cache.GetReceiver("retired", new());
        await cache.RemoveEntityAsync("retired");
        await Assert.That(receiver.IsClosed).IsTrue();
        var replacement = cache.GetReceiver("retired", new());
        await Assert.That(ReferenceEquals(receiver, replacement)).IsFalse();
        await Assert.That(replacement.IsClosed).IsFalse();
        try
        {
            await using var unexpectedLock = await cache.LockAsync(receiver);
            throw new InvalidOperationException("Retired receivers must not be used by queued requests.");
        }
        catch (ServiceBusException exception)
        {
            await Assert.That(exception.Reason).IsEqualTo(ServiceBusFailureReason.MessagingEntityNotFound);
        }
    }

    [Test]
    [Arguments("/api/queues/cursor/messages", "cursor")]
    [Arguments("/api/topics/cursor/subscriptions/sub/messages", "cursor/Subscriptions/sub")]
    public async Task CursorPeekingDoesNotRescanEarlierMessages(string path, string entityPath)
    {
        var broker = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        for (var i = 0; i < 30; i++) broker.AddDeadLetterMessage(entityPath, $"message-{i}", "body");
        var client = Factory.CreateClient();
        var page = await client.GetFromJsonAsync<PagedMessages>(path + "?mode=peek&state=deadletter&take=25");
        await Assert.That(page!.HasMore).IsTrue();
        await Assert.That(page.NextSequenceNumber).IsEqualTo("26");
        var calls = broker.PeekCallCount;
        var next = await client.GetFromJsonAsync<PagedMessages>(path + $"?mode=peek&state=deadletter&take=25&fromSequenceNumber={page.NextSequenceNumber}");
        await Assert.That(next!.Items.Count).IsEqualTo(5);
        await Assert.That(next.Items[0].MessageId).IsEqualTo("message-25");
        await Assert.That(next.HasMore).IsFalse();
        await Assert.That(broker.PeekCallCount - calls).IsEqualTo(2);
    }
}
