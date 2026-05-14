using System.Diagnostics.CodeAnalysis;
using Azure;
using Azure.Core;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.

namespace ServiceBusEmulatorExplorer.Tests;

[SuppressMessage("Compiler", "CS8625:Cannot convert null literal to non-nullable reference type.")]
public class TestServiceBusClient : ServiceBusClient
{
    private readonly Dictionary<string, TestServiceBusReceiver> _queueReceivers = [];
    private readonly Dictionary<string, TestServiceBusReceiver> _topicReceivers = [];
    private readonly Dictionary<string, TestServiceBusSender> _senders = [];


    public override ServiceBusSender CreateSender(string queueOrTopicName)
    {
        var sender = new TestServiceBusSender(queueOrTopicName);
        _senders[queueOrTopicName] = sender;

        return sender;
    }

    public override ServiceBusSender CreateSender(string queueOrTopicName, ServiceBusSenderOptions options) => CreateSender(queueOrTopicName);

    public override ServiceBusReceiver CreateReceiver(string queueName,
        ServiceBusReceiverOptions receiverOptions = null)
    {
        var receiver = new TestServiceBusReceiver(queueName, this);
        _queueReceivers[queueName] = receiver;

        return receiver;
    }

    public override ServiceBusReceiver CreateReceiver(string topicName, string subscriptionName,
        ServiceBusReceiverOptions options)
    {
        var key = $"{topicName}/Subscriptions/{subscriptionName}";
        var receiver = new TestServiceBusReceiver(key, this);
        _topicReceivers[key] = receiver;

        return receiver;
    }


    private class TestServiceBusSender(string entityPath) : ServiceBusSender
    {
        public override string EntityPath => entityPath;

        internal List<ServiceBusMessage> Messages { get; } = [];
        
        
        public override Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken = new())
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    public class TestServiceBusReceiver(string entityPath, TestServiceBusClient client) : ServiceBusReceiver
    {
        public override string EntityPath => entityPath;


        public override Task<IReadOnlyList<ServiceBusReceivedMessage>> PeekMessagesAsync(int maxMessages, long? fromSequenceNumber = null,
            CancellationToken cancellationToken = new())
        {
            var sender = client._senders.GetValueOrDefault(entityPath) ?? throw new InvalidOperationException($"No sender found for entity path '{entityPath}'");
            
            var messages = sender.Messages
                .Skip((int)(fromSequenceNumber ?? 0))
                .Take(maxMessages)
                .Select(m => ServiceBusModelFactory.ServiceBusReceivedMessage(
                    body: m.Body,
                    messageId: m.MessageId,
                    sequenceNumber: sender.Messages.IndexOf(m) + 1))
                .ToList()
                .AsReadOnly();
            
            return Task.FromResult<IReadOnlyList<ServiceBusReceivedMessage>>(messages);

        }
    }
}

public class TestServiceBusAdministrationClient : ServiceBusAdministrationClient
{
    private readonly Dictionary<string, CreateQueueOptions> _queues = [];
    private readonly Dictionary<string, CreateTopicOptions> _topics = [];
    private readonly Dictionary<string, CreateSubscriptionOptions> _subscriptions = [];
    
    public override Task<Response> DeleteQueueAsync(string name, CancellationToken cancellationToken = new())
    {
        if (_queues.GetValueOrDefault(name) is not null)
        {
            _queues.Remove(name);
            return Task.FromResult<Response>(new TestResponse(200));
        }
        
        return Task.FromResult<Response>(new TestResponse(404));
    }

    public override Task<Response<QueueProperties>> GetQueueAsync(string name, CancellationToken cancellationToken = new())
    {
        if (_queues.GetValueOrDefault(name) is null)
        {
            return Task.FromResult(Response.FromValue<QueueProperties>(null!, new TestResponse(404)));
        }
        
        var queueProperties = ServiceBusModelFactory.QueueProperties(
            name: name,
            lockDuration: TimeSpan.FromMinutes(1),
            maxDeliveryCount: 10,
            defaultMessageTimeToLive: TimeSpan.FromDays(14),
            autoDeleteOnIdle: TimeSpan.MaxValue,
            duplicateDetectionHistoryTimeWindow: TimeSpan.FromDays(1),
            userMetadata:"");

        return Task.FromResult(Response.FromValue(queueProperties, new TestResponse(200)));
    }

    public override Task<Response<QueueProperties>> CreateQueueAsync(string name, CancellationToken cancellationToken = new()) => CreateQueueAsync(new CreateQueueOptions(name), cancellationToken);

    public override Task<Response<QueueProperties>> CreateQueueAsync(CreateQueueOptions options, CancellationToken cancellationToken = new())
    {
        if (_queues.GetValueOrDefault(options.Name) is not null)
        {
            return Task.FromResult(Response.FromValue<QueueProperties>(null!, new TestResponse(409)));
        }
        
        var queueProperties = ServiceBusModelFactory.QueueProperties(
            name: options.Name,
            lockDuration: options.LockDuration,
            maxDeliveryCount: options.MaxDeliveryCount,
            defaultMessageTimeToLive: options.DefaultMessageTimeToLive,
            autoDeleteOnIdle: TimeSpan.MaxValue,
            duplicateDetectionHistoryTimeWindow: TimeSpan.FromDays(1),
            userMetadata:"");

        _queues[options.Name] = options;

        return Task.FromResult(Response.FromValue(queueProperties, new TestResponse(201)));
    }

    public override AsyncPageable<QueueRuntimeProperties> GetQueuesRuntimePropertiesAsync(CancellationToken cancellationToken = new()) =>
        AsyncPageable<QueueRuntimeProperties>.FromPages(
        [
            Page<QueueRuntimeProperties>.FromValues(
                _queues.Select(q => ServiceBusModelFactory.QueueRuntimeProperties(
                    name: q.Key,
                    activeMessageCount: 0,
                    deadLetterMessageCount: 0,
                    scheduledMessageCount: 0,
                    transferMessageCount: 0,
                    transferDeadLetterMessageCount: 0,
                    sizeInBytes: 0,
                    createdAt: DateTimeOffset.UtcNow,
                    updatedAt: DateTimeOffset.UtcNow,
                    accessedAt: DateTimeOffset.UtcNow)).ToList(),
                continuationToken: null,
                response: new TestResponse(200))
        ]);
    

    public override Task<Response<SubscriptionProperties>> CreateSubscriptionAsync(CreateSubscriptionOptions options,
        CancellationToken cancellationToken = new())
    {
        if(!_topics.ContainsKey(options.TopicName))
        {
            return Task.FromResult(
                Response.FromValue<SubscriptionProperties>(null!, new TestResponse(404)));
        }
        
        var subscriptionProperties = ServiceBusModelFactory.SubscriptionProperties(
            topicName: options.TopicName,
            subscriptionName: options.SubscriptionName,
            lockDuration: options.LockDuration,
            maxDeliveryCount: options.MaxDeliveryCount,
            defaultMessageTimeToLive: options.DefaultMessageTimeToLive,
            autoDeleteOnIdle: TimeSpan.MaxValue,
            userMetadata: "");
        
        _subscriptions[$"{options.TopicName}/Subscriptions/{options.SubscriptionName}"] = options;
        return Task.FromResult(Response.FromValue(subscriptionProperties, new TestResponse(201)));
    }

    public override Task<Response> DeleteSubscriptionAsync(string topicName, string subscriptionName,
        CancellationToken cancellationToken = new())
    {
        var key = $"{topicName}/Subscriptions/{subscriptionName}";
        if (_subscriptions.GetValueOrDefault(key) is not null)
        {
            _subscriptions.Remove(key);
            return Task.FromResult<Response>(new TestResponse(200));
        }
        
        return Task.FromResult<Response>(new TestResponse(404));
    }

    public override Task<Response<SubscriptionProperties>> GetSubscriptionAsync(string topicName, string subscriptionName,
        CancellationToken cancellationToken = new())
    {
        var key = $"{topicName}/Subscriptions/{subscriptionName}";
        var options = _subscriptions.GetValueOrDefault(key);
        if (options is null)
        {
            throw new ServiceBusException($"Subscription '{subscriptionName}' not found on topic '{topicName}'.", ServiceBusFailureReason.MessagingEntityNotFound);
        }
        
        var subscriptionProperties = ServiceBusModelFactory.SubscriptionProperties(
            topicName: topicName,
            subscriptionName: subscriptionName,
            lockDuration: options.LockDuration,
            defaultMessageTimeToLive: options.DefaultMessageTimeToLive,
            maxDeliveryCount: options.MaxDeliveryCount,
            autoDeleteOnIdle: TimeSpan.MaxValue,
            userMetadata: "");
        
        return Task.FromResult(Response.FromValue(subscriptionProperties, new TestResponse(200)));
    }

    public override AsyncPageable<SubscriptionRuntimeProperties> GetSubscriptionsRuntimePropertiesAsync(string topicName,
        CancellationToken cancellationToken = new())
    {
        var subscriptions = _subscriptions
            .Where(kvp => kvp.Key.StartsWith($"{topicName}/Subscriptions/"))
            .Select(kvp => ServiceBusModelFactory.SubscriptionRuntimeProperties(
                topicName: topicName,
                subscriptionName: kvp.Value.SubscriptionName,
                activeMessageCount: 0,
                deadLetterMessageCount: 0,
                transferMessageCount: 0,
                transferDeadLetterMessageCount: 0,
                createdAt: DateTimeOffset.UtcNow,
                updatedAt: DateTimeOffset.UtcNow,
                accessedAt: DateTimeOffset.UtcNow))
            .ToList();

        return AsyncPageable<SubscriptionRuntimeProperties>.FromPages(
        [
            Page<SubscriptionRuntimeProperties>.FromValues(
                subscriptions,
                continuationToken: null,
                response: new TestResponse(200))
        ]);
    }

    public override AsyncPageable<TopicRuntimeProperties> GetTopicsRuntimePropertiesAsync(CancellationToken cancellationToken = new()) =>
        AsyncPageable<TopicRuntimeProperties>.FromPages(
        [
            Page<TopicRuntimeProperties>.FromValues(
                _topics.Select(t => ServiceBusModelFactory.TopicRuntimeProperties(
                    name: t.Key,
                    sizeInBytes: 0,
                    createdAt: DateTimeOffset.UtcNow,
                    updatedAt: DateTimeOffset.UtcNow,
                    accessedAt: DateTimeOffset.UtcNow)).ToList(),
                continuationToken: null,
                response: new TestResponse(200))
        ]);

    public override Task<Response<TopicProperties>> CreateTopicAsync(CreateTopicOptions options, CancellationToken cancellationToken = new())
    {
        if (_topics.GetValueOrDefault(options.Name) is not null)
        {
            return Task.FromResult(
                Response.FromValue<TopicProperties>(null!, new TestResponse(409)));
        }
        
        var topicProperties = ServiceBusModelFactory.TopicProperties(
            name: options.Name,
            defaultMessageTimeToLive: options.DefaultMessageTimeToLive,
            autoDeleteOnIdle: TimeSpan.MaxValue,
            duplicateDetectionHistoryTimeWindow: TimeSpan.FromDays(1));
        _topics[options.Name] = options;
        return Task.FromResult(Response.FromValue(topicProperties, new TestResponse(201)));
    }

    public override Task<Response> DeleteTopicAsync(string name, CancellationToken cancellationToken = new())
    {
        if (_topics.GetValueOrDefault(name) is not null)
        {
            _topics.Remove(name);
            return Task.FromResult<Response>(new TestResponse(200));
        }
        
        return Task.FromResult<Response>(new TestResponse(404));
    }

    public override Task<Response<TopicProperties>> GetTopicAsync(string name, CancellationToken cancellationToken = new())
    {
        if (_topics.GetValueOrDefault(name) is null)
        {
            return Task.FromResult(
                Response.FromValue<TopicProperties>(null!, new TestResponse(404)));
        }
        
        var topicProperties = ServiceBusModelFactory.TopicProperties(
            name: name,
            defaultMessageTimeToLive: TimeSpan.FromDays(14),
            autoDeleteOnIdle: TimeSpan.MaxValue,
            duplicateDetectionHistoryTimeWindow: TimeSpan.FromDays(1));
        
        return Task.FromResult(Response.FromValue(topicProperties, new TestResponse(200)));
    }

    private class TestResponse(int status) : Response
    {
        public override int Status => status;

        public override string ReasonPhrase => throw new NotImplementedException();

        public override Stream? ContentStream { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public override string ClientRequestId { get; set; } = Guid.NewGuid().ToString();

        public override void Dispose()
        {
        }

        protected override bool TryGetHeader(string name, [NotNullWhen(true)] out string? value)
        {
            throw new NotImplementedException();
        }

        protected override bool TryGetHeaderValues(string name, [NotNullWhen(true)] out IEnumerable<string>? values)
        {
            throw new NotImplementedException();
        }

        protected override bool ContainsHeader(string name)
        {
            throw new NotImplementedException();
        }

        protected override IEnumerable<HttpHeader> EnumerateHeaders()
        {
            throw new NotImplementedException();
        }
    }
}