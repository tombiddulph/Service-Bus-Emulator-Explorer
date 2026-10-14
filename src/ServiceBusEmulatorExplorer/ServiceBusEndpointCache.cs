using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;

namespace ServiceBusEmulatorExplorer;

public sealed class ServiceBusEndpointCache(ServiceBusClient client) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new();
    private readonly object _creationLock = new();
    private readonly ConcurrentDictionary<string, ServiceBusReceiver> _receivers = new();
    // Canonical entity+subqueue key per receiver, so operations against the same DLQ share a lock
    // regardless of which receive mode (PeekLock vs ReceiveAndDelete) created the receiver instance.
    private sealed record OperationKey(string Value);
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ServiceBusReceiver, OperationKey> _receiverOperationKeys = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _operationLocks = new();
    private sealed class CountEntry
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public DateTimeOffset ExpiresAt;
        public int Generation = -1;
        public MessageCountResult Result;
    }
    private readonly ConcurrentDictionary<string, CountEntry> _counts = new();
    private int _countGeneration;

    public void InvalidateCounts() => Interlocked.Increment(ref _countGeneration);

    public async Task<MessageCountResult> GetCountAsync(ServiceBusReceiver receiver, CancellationToken cancellationToken = default)
    {
        var key = _receiverOperationKeys.GetValue(receiver, r => new OperationKey(r.EntityPath)).Value;
        var entry = _counts.GetOrAdd(key, _ => new CountEntry());
        await entry.Gate.WaitAsync(cancellationToken);
        try
        {
            var generation = Volatile.Read(ref _countGeneration);
            if (entry.Generation == generation && entry.ExpiresAt > DateTimeOffset.UtcNow) return entry.Result;
            var result = await Helpers.CountMessagesAsync(this, receiver, cancellationToken: cancellationToken);
            entry.Result = result;
            entry.Generation = generation;
            entry.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(2);
            return result;
        }
        finally { entry.Gate.Release(); }
    }

    public async Task RemoveEntityAsync(string entityPath)
    {
        InvalidateCounts();
        foreach (var (key, receiver) in _receivers.ToArray())
        {
            if (receiver.EntityPath != entityPath && !receiver.EntityPath.StartsWith(entityPath + "/", StringComparison.Ordinal)) continue;
            await using var operationLock = await LockAsync(receiver);
            if (_receivers.TryRemove(new KeyValuePair<string, ServiceBusReceiver>(key, receiver)))
                await receiver.DisposeAsync();
        }
        if (_senders.TryRemove(entityPath, out var sender)) await sender.DisposeAsync();
        // Keep canonical lock identities: already queued operations must never acquire a different lock.
    }

    public ServiceBusSender GetSender(string queue)
    {
        lock (_creationLock) return _senders.GetOrAdd(queue, client.CreateSender);
    }

    public ServiceBusReceiver GetReceiver(string queue, ServiceBusReceiverOptions receiverOptions)
    {
        var key = $"{queue}-{receiverOptions.ReceiveMode}-{receiverOptions.SubQueue}";
        ServiceBusReceiver receiver;
        lock (_creationLock) receiver = _receivers.GetOrAdd(key, _ => client.CreateReceiver(queue, receiverOptions));
        _receiverOperationKeys.GetValue(receiver, _ => new OperationKey($"{queue}-{receiverOptions.SubQueue}"));
        return receiver;
    }

    public ServiceBusReceiver GetTopicReceiver(
        string topic,
        string subscription,
        ServiceBusReceiverOptions receiverOptions)
    {
        var entityPath = $"{topic}/Subscriptions/{subscription}";
        var key = $"{entityPath}-{receiverOptions.ReceiveMode}-{receiverOptions.SubQueue}";
        ServiceBusReceiver receiver;
        lock (_creationLock) receiver = _receivers.GetOrAdd(key, _ => client.CreateReceiver(topic, subscription, receiverOptions));
        _receiverOperationKeys.GetValue(receiver, _ => new OperationKey($"{entityPath}-{receiverOptions.SubQueue}"));
        return receiver;
    }

    // Destructive drains must close their receive link before releasing the entity lock:
    // an idle ReceiveAndDelete link can retain credit and consume messages sent later.
    public Task<ReceiverScope> CreateReceiverScopeAsync(
        string queue, ServiceBusReceiverOptions options, CancellationToken cancellationToken = default) =>
        CreateReceiverScopeAsync(queue, options, () => client.CreateReceiver(queue, options), cancellationToken);

    public Task<ReceiverScope> CreateTopicReceiverScopeAsync(
        string topic, string subscription, ServiceBusReceiverOptions options, CancellationToken cancellationToken = default) =>
        CreateReceiverScopeAsync($"{topic}/Subscriptions/{subscription}", options,
            () => client.CreateReceiver(topic, subscription, options), cancellationToken);

    private async Task<ReceiverScope> CreateReceiverScopeAsync(
        string entityPath, ServiceBusReceiverOptions options, Func<ServiceBusReceiver> createReceiver,
        CancellationToken cancellationToken)
    {
        var operationLock = await LockAsync($"{entityPath}-{options.SubQueue}", cancellationToken);
        try
        {
            return new ReceiverScope(createReceiver(), operationLock);
        }
        catch
        {
            await operationLock.DisposeAsync();
            throw;
        }
    }

    public sealed class ReceiverScope(ServiceBusReceiver receiver, IAsyncDisposable operationLock) : IAsyncDisposable
    {
        public ServiceBusReceiver Receiver { get; } = receiver;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Receiver.DisposeAsync();
            }
            finally
            {
                await operationLock.DisposeAsync();
            }
        }
    }

    // Receivers are cached and shared across concurrent HTTP requests (e.g. the periodic message-list
    // poll racing with a peek/lock/complete replay), but ServiceBusReceiver isn't safe for concurrent use.
    // Callers must hold this lock for the duration of any operation against a given receiver.
    public async Task<IAsyncDisposable> LockAsync(ServiceBusReceiver receiver, CancellationToken cancellationToken = default)
    {
        var operationKey = _receiverOperationKeys.GetValue(receiver, r => new OperationKey(r.EntityPath)).Value;
        return await LockAsync(operationKey, cancellationToken);
    }

    private async Task<IAsyncDisposable> LockAsync(string operationKey, CancellationToken cancellationToken)
    {
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
