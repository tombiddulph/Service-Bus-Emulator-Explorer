using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;

namespace ServiceBusEmulatorExplorer;

public sealed class ServiceBusEndpointCache(ServiceBusClient client) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new();
    private readonly ConcurrentDictionary<string, ServiceBusReceiver> _receivers = new();
    // Canonical entity+subqueue key per receiver, so operations against the same DLQ share a lock
    // regardless of which receive mode (PeekLock vs ReceiveAndDelete) created the receiver instance.
    private readonly ConcurrentDictionary<ServiceBusReceiver, string> _receiverOperationKeys = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _operationLocks = new();

    public ServiceBusSender GetSender(string queue) =>
        _senders.GetOrAdd(queue, client.CreateSender);

    public ServiceBusReceiver GetReceiver(string queue, ServiceBusReceiverOptions receiverOptions)
    {
        var key = $"{queue}-{receiverOptions.ReceiveMode}-{receiverOptions.SubQueue}";
        var receiver = _receivers.GetOrAdd(key, _ => client.CreateReceiver(queue, receiverOptions));
        _receiverOperationKeys.TryAdd(receiver, $"{queue}-{receiverOptions.SubQueue}");
        return receiver;
    }

    public ServiceBusReceiver GetTopicReceiver(
        string topic,
        string subscription,
        ServiceBusReceiverOptions receiverOptions)
    {
        var entityPath = $"{topic}/Subscriptions/{subscription}";
        var key = $"{entityPath}-{receiverOptions.ReceiveMode}-{receiverOptions.SubQueue}";
        var receiver = _receivers.GetOrAdd(key, _ => client.CreateReceiver(topic, subscription, receiverOptions));
        _receiverOperationKeys.TryAdd(receiver, $"{entityPath}-{receiverOptions.SubQueue}");
        return receiver;
    }

    // Receivers are cached and shared across concurrent HTTP requests (e.g. the periodic message-list
    // poll racing with a peek/lock/complete replay), but ServiceBusReceiver isn't safe for concurrent use.
    // Callers must hold this lock for the duration of any operation against a given receiver.
    public async Task<IAsyncDisposable> LockAsync(ServiceBusReceiver receiver, CancellationToken cancellationToken = default)
    {
        var operationKey = _receiverOperationKeys.GetOrAdd(receiver, r => $"{r.EntityPath}");
        var semaphore = _operationLocks.GetOrAdd(operationKey, _ => new SemaphoreSlim(1, 1));
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
        foreach (var semaphore in _operationLocks.Values)
            semaphore.Dispose();
    }
}