using System.Collections.Concurrent;
using Azure.Core;
using Azure.Messaging.ServiceBus;

namespace SbExplorer.Mud.Services;

public class CachingServiceBusClient : ServiceBusClient
{

    public CachingServiceBusClient(string connectionString) : base(connectionString)
    {

    }

    public CachingServiceBusClient(string connectionString, ServiceBusClientOptions options) : base(connectionString, options)
    {

    }

    public CachingServiceBusClient(string fullyQualifiedNamespace, TokenCredential credential) : base(fullyQualifiedNamespace, credential)
    {
        throw new NotImplementedException("This constructor is not supported");
    }

    public CachingServiceBusClient(string fullyQualifiedNamespace, TokenCredential credential, ServiceBusClientOptions options) : base(fullyQualifiedNamespace, credential, options)
    {
        throw new NotImplementedException("This constructor is not supported");
    }

    private readonly ConcurrentDictionary<string, ServiceBusProcessor> _processors = new();
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new();
    private readonly ConcurrentDictionary<string, ServiceBusReceiver> _receivers = new();


    public override ServiceBusProcessor CreateProcessor(string topicName, string subscriptionName) => _processors.GetOrAdd($"{topicName}/{subscriptionName}", base.CreateProcessor(topicName, subscriptionName));

    public override ServiceBusProcessor CreateProcessor(string queueName, ServiceBusProcessorOptions options) => _processors.GetOrAdd(queueName, base.CreateProcessor(queueName, options));

    public override ServiceBusProcessor CreateProcessor(string queueName) => _processors.GetOrAdd(queueName, base.CreateProcessor(queueName));

    public override ServiceBusProcessor CreateProcessor(string topicName, string subscriptionName, ServiceBusProcessorOptions options) => _processors.GetOrAdd($"{topicName}/{subscriptionName}", base.CreateProcessor(topicName, subscriptionName, options));

    public override ServiceBusReceiver CreateReceiver(string queueName, ServiceBusReceiverOptions options) => _receivers.GetOrAdd(options.SubQueue switch
    {
        SubQueue.DeadLetter => $"{queueName}/deadletter",
        SubQueue.TransferDeadLetter => $"{queueName}/transferdeadletter",
        SubQueue.None => queueName,
        _ => throw new ArgumentOutOfRangeException()
    }, base.CreateReceiver(queueName, options));

    public override ServiceBusReceiver CreateReceiver(string topicName, string subscriptionName) => _receivers.GetOrAdd($"{topicName}/{subscriptionName}", base.CreateReceiver(topicName, subscriptionName));

    public override ServiceBusReceiver CreateReceiver(string topicName, string subscriptionName, ServiceBusReceiverOptions options) => _receivers.GetOrAdd($"{topicName}/{subscriptionName}", base.CreateReceiver(topicName, subscriptionName, options));

    public override ServiceBusReceiver CreateReceiver(string queueName) => _receivers.GetOrAdd(queueName, base.CreateReceiver(queueName));

    public override ServiceBusSender CreateSender(string queueName) => _senders.GetOrAdd(queueName, base.CreateSender(queueName));

    public override ServiceBusSender CreateSender(string queueOrTopicName, ServiceBusSenderOptions options) => _senders.GetOrAdd(queueOrTopicName, base.CreateSender(queueOrTopicName, options));


}