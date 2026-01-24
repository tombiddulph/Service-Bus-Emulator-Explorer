using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;

namespace ServiceBusEmulatorExplorer;

public sealed class ServiceBusEndpointCache(ServiceBusClient client) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new();
    private readonly ConcurrentDictionary<string, ServiceBusReceiver> _receivers = new();

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

    public async ValueTask DisposeAsync()
    {
        foreach (var sender in _senders.Values)
            await sender.DisposeAsync();
        foreach (var receiver in _receivers.Values)
            await receiver.DisposeAsync();
    }
}