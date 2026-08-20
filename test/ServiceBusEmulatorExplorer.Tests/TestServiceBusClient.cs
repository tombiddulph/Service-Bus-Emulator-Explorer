using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
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
    private readonly Dictionary<string, List<ServiceBusReceivedMessage>> _deadLetterMessages = [];
    private readonly Dictionary<string, PurgeReceiverBehavior> _purgeBehaviors = [];

    // Test hook: makes CompleteMessageAsync simulate a cancelled/timed-out settlement for this message id.
    public string? FailCompleteForMessageId { get; set; }
    public string? BlockAbandonForMessageId { get; set; }
    public TaskCompletionSource AbandonStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource AllowAbandon { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TimeSpan? SendDelay { get; set; }
    public TimeSpan? SimulatedLockDuration { get; set; }
    public int RenewLockCallCount { get; private set; }

    public void AddActiveMessage(string entityPath, string messageId, string body)
    {
        if (!_senders.TryGetValue(entityPath, out var sender))
        {
            sender = new TestServiceBusSender(entityPath, this);
            _senders[entityPath] = sender;
        }

        sender.Messages.Add(new ServiceBusMessage(body) { MessageId = messageId });
    }

    public void ConfigurePurgeReceiver(
        string entityPath,
        int batchSize,
        int? failAfterCalls = null,
        TimeSpan? delay = null,
        int delayAfterCalls = 0) =>
        _purgeBehaviors[entityPath] = new PurgeReceiverBehavior(batchSize, failAfterCalls, delay, delayAfterCalls);


    public override ServiceBusSender CreateSender(string queueOrTopicName)
    {
        var sender = new TestServiceBusSender(queueOrTopicName, this);
        _senders[queueOrTopicName] = sender;

        return sender;
    }

    public override ServiceBusSender CreateSender(string queueOrTopicName, ServiceBusSenderOptions options) => CreateSender(queueOrTopicName);

    public override ServiceBusReceiver CreateReceiver(string queueName,
        ServiceBusReceiverOptions receiverOptions = null)
    {
        var receiver = new TestServiceBusReceiver(queueName, this, receiverOptions?.SubQueue == SubQueue.DeadLetter);
        _queueReceivers[queueName] = receiver;

        return receiver;
    }

    public override ServiceBusReceiver CreateReceiver(string topicName, string subscriptionName,
        ServiceBusReceiverOptions options)
    {
        var key = $"{topicName}/Subscriptions/{subscriptionName}";
        var receiver = new TestServiceBusReceiver(key, this, options.SubQueue == SubQueue.DeadLetter);
        _topicReceivers[key] = receiver;

        return receiver;
    }

    public void AddDeadLetterMessage(string entityPath, string messageId, string body, string? contentType = null,
        IDictionary<string, object>? applicationProperties = null, string? partitionKey = null, string? sessionId = null,
        TimeSpan? timeToLive = null, string? correlationId = null, string? subject = null, string? replyTo = null)
    {
        if (!_deadLetterMessages.TryGetValue(entityPath, out var messages))
        {
            messages = [];
            _deadLetterMessages[entityPath] = messages;
        }

        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString(body),
            messageId: messageId,
            partitionKey: partitionKey,
            sessionId: sessionId,
            timeToLive: timeToLive ?? TimeSpan.FromMinutes(5),
            correlationId: correlationId,
            subject: subject,
            contentType: contentType,
            replyTo: replyTo,
            properties: applicationProperties,
            sequenceNumber: messages.Count + 1,
            lockedUntil: SimulatedLockDuration is { } duration ? DateTimeOffset.UtcNow + duration : DateTimeOffset.UtcNow.AddMinutes(1));

        messages.Add(message);
    }

    public IReadOnlyList<ServiceBusMessage> GetSentMessages(string entityPath) =>
        _senders.GetValueOrDefault(entityPath)?.Messages ?? [];

    public IReadOnlyList<ServiceBusReceivedMessage> GetDeadLetterMessages(string entityPath) =>
        _deadLetterMessages.GetValueOrDefault(entityPath) ?? [];

    private sealed record PurgeReceiverBehavior(
        int BatchSize,
        int? FailAfterCalls,
        TimeSpan? Delay,
        int DelayAfterCalls)
    {
        public int Calls { get; set; }
    }

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private class TestServiceBusSender(string entityPath, TestServiceBusClient client) : ServiceBusSender
    {
        public override string EntityPath => entityPath;

        internal List<ServiceBusMessage> Messages { get; } = [];
        
        
        public override async Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken = new())
        {
            if (client.SendDelay is { } delay)
                await Task.Delay(delay, cancellationToken);
            Messages.Add(message);
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public class TestServiceBusReceiver(string entityPath, TestServiceBusClient client, bool isDeadLetterReceiver) : ServiceBusReceiver
    {
        private readonly HashSet<long> _locked = [];
        private readonly System.Collections.Concurrent.ConcurrentDictionary<long, DateTimeOffset> _lockedUntil = [];

        public override string EntityPath => entityPath;

        private List<ServiceBusReceivedMessage> DeadLetterMessages =>
            client._deadLetterMessages.GetValueOrDefault(entityPath) ?? [];

        public override Task<IReadOnlyList<ServiceBusReceivedMessage>> PeekMessagesAsync(int maxMessages, long? fromSequenceNumber = null,
            CancellationToken cancellationToken = new())
        {
            // Peeking sees locked messages too, so this deliberately ignores _locked.
            if (isDeadLetterReceiver)
            {
                var deadLettered = DeadLetterMessages
                    .Where(m => m.SequenceNumber >= (fromSequenceNumber ?? 0))
                    .Take(maxMessages)
                    .ToList();

                return Task.FromResult<IReadOnlyList<ServiceBusReceivedMessage>>(deadLettered);
            }

            var sender = client._senders.GetValueOrDefault(entityPath);
            if (sender is null)
                return Task.FromResult<IReadOnlyList<ServiceBusReceivedMessage>>([]);

            var messages = sender.Messages
                .Skip((int)(fromSequenceNumber ?? 0))
                .Take(maxMessages)
                .Select(m => ServiceBusModelFactory.ServiceBusReceivedMessage(
                    body: m.Body,
                    messageId: m.MessageId,
                    contentType: m.ContentType,
                    sessionId: m.SessionId,
                    properties: m.ApplicationProperties,
                    sequenceNumber: sender.Messages.IndexOf(m) + 1))
                .ToList()
                .AsReadOnly();

            return Task.FromResult<IReadOnlyList<ServiceBusReceivedMessage>>(messages);
        }

        public override async Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveMessagesAsync(int maxMessages,
            TimeSpan? maxWaitTime = null, CancellationToken cancellationToken = new())
        {
            if (!isDeadLetterReceiver)
            {
                var behavior = client._purgeBehaviors.GetValueOrDefault(entityPath);
                if (behavior is not null)
                {
                    behavior.Calls++;
                    if (behavior.FailAfterCalls is not null && behavior.Calls > behavior.FailAfterCalls)
                        throw new ServiceBusException("Simulated receiver failure", ServiceBusFailureReason.ServiceCommunicationProblem);
                    if (behavior.Delay is not null && behavior.Calls > behavior.DelayAfterCalls)
                        await Task.Delay(behavior.Delay.Value, cancellationToken);
                    maxMessages = Math.Min(maxMessages, behavior.BatchSize);
                }

                var sender = client._senders.GetValueOrDefault(entityPath);
                if (sender is null)
                    return [];

                var activeMessages = sender.Messages
                    .Take(maxMessages)
                    .Select((message, index) => ServiceBusModelFactory.ServiceBusReceivedMessage(
                        body: message.Body,
                        messageId: message.MessageId,
                        contentType: message.ContentType,
                        sequenceNumber: index + 1))
                    .ToList();

                sender.Messages.RemoveRange(0, activeMessages.Count);
                return activeMessages;
            }

            var messages = DeadLetterMessages
                .Where(m => !_locked.Contains(m.SequenceNumber))
                .Take(maxMessages)
                .ToList();

            foreach (var message in messages)
            {
                _locked.Add(message.SequenceNumber);
                _lockedUntil[message.SequenceNumber] = DateTimeOffset.UtcNow + (client.SimulatedLockDuration ?? TimeSpan.FromMinutes(1));
            }

            return messages;
        }

        public override Task CompleteMessageAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken = new())
        {
            if (message.MessageId == client.FailCompleteForMessageId)
                throw new OperationCanceledException("Simulated settlement timeout");
            if (_lockedUntil.GetValueOrDefault(message.SequenceNumber) < DateTimeOffset.UtcNow)
                throw new OperationCanceledException("Simulated message lock expired");

            client._deadLetterMessages.GetValueOrDefault(entityPath)?.Remove(message);
            _locked.Remove(message.SequenceNumber);
            return Task.CompletedTask;
        }

        public override Task RenewMessageLockAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken = new())
        {
            cancellationToken.ThrowIfCancellationRequested();
            _lockedUntil[message.SequenceNumber] = DateTimeOffset.UtcNow + (client.SimulatedLockDuration ?? TimeSpan.FromMinutes(1));
            client.RenewLockCallCount++;
            return Task.CompletedTask;
        }

        // Drains and removes every remaining message, simulating a ReceiveAndDelete purge loop.
        public override async IAsyncEnumerable<ServiceBusReceivedMessage> ReceiveMessagesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (isDeadLetterReceiver)
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var next = client._deadLetterMessages.GetValueOrDefault(entityPath)?.FirstOrDefault(m => !_locked.Contains(m.SequenceNumber));
                    if (next is null) yield break;
                    client._deadLetterMessages.GetValueOrDefault(entityPath)?.Remove(next);
                    yield return next;
                    await Task.Yield();
                }
            }
            else
            {
                var sender = client._senders.GetValueOrDefault(entityPath);
                if (sender is null) yield break;

                while (sender.Messages.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var m = sender.Messages[0];
                    sender.Messages.RemoveAt(0);
                    yield return ServiceBusModelFactory.ServiceBusReceivedMessage(
                        body: m.Body,
                        messageId: m.MessageId,
                        contentType: m.ContentType,
                        sessionId: m.SessionId,
                        properties: m.ApplicationProperties,
                        sequenceNumber: 1);
                    await Task.Yield();
                }
            }
        }

        public override async Task AbandonMessageAsync(ServiceBusReceivedMessage message,
            IDictionary<string, object>? propertiesToModify = null, CancellationToken cancellationToken = new())
        {
            if (message.MessageId == client.BlockAbandonForMessageId)
            {
                client.AbandonStarted.TrySetResult();
                await client.AllowAbandon.Task;
            }
            _locked.Remove(message.SequenceNumber);
            _lockedUntil.TryRemove(message.SequenceNumber, out _);
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
            userMetadata:"",
            requiresSession: _queues[name].RequiresSession);

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
            userMetadata:"",
            requiresSession: options.RequiresSession);

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
            userMetadata: "",
            requiresSession: options.RequiresSession);
        
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
            userMetadata: "",
            requiresSession: options.RequiresSession);
        
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
