using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;

namespace ServiceBusEmulatorExplorer;

public sealed class ServiceBusEndpointCache(ServiceBusClient client) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new();
    private readonly ConcurrentDictionary<string, ServiceBusReceiver> _receivers = new();
    private readonly ConcurrentDictionary<ServiceBusReceiver, SemaphoreSlim> _receiverLocks = new();

    public ServiceBusSender GetSender(string queue) =>
        _senders.GetOrAdd(queue, client.CreateSender);

    public ServiceBusReceiver GetReceiver(string queue, ServiceBusReceiverOptions receiverOptions)
    {
        var key = $"{queue}-{receiverOptions.ReceiveMode}-{receiverOptions.SubQueue}";
        return _receivers.GetOrAdd(key, _ => client.CreateReceiver(queue, receiverOptions));
    }

    public ServiceBusReceiver GetTopicReceiver(
        string topic,
        string subscription,
        ServiceBusReceiverOptions receiverOptions)
    {
        var entityPath = $"{topic}/Subscriptions/{subscription}";
        var key = $"{entityPath}-{receiverOptions.ReceiveMode}-{receiverOptions.SubQueue}";
        return _receivers.GetOrAdd(key, _ => client.CreateReceiver(topic, subscription, receiverOptions));
    }

    // Receivers are cached and shared across concurrent HTTP requests (e.g. the periodic message-list
    // poll racing with a peek/lock/complete replay), but ServiceBusReceiver isn't safe for concurrent use.
    // Callers must hold this lock for the duration of any operation against a given receiver.
    public async Task<IAsyncDisposable> LockAsync(ServiceBusReceiver receiver, CancellationToken cancellationToken = default)
    {
        var semaphore = _receiverLocks.GetOrAdd(receiver, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var sender in _senders.Values)
            await sender.DisposeAsync();
        foreach (var receiver in _receivers.Values)
            await receiver.DisposeAsync();
        foreach (var semaphore in _receiverLocks.Values)
            semaphore.Dispose();
    }
}