using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using SbExplorer.Mud.Helpers;

namespace SbExplorer.Mud.Services;

public class ServiceBusAdmin(
    IOptions<EmulatorConfig> emulatorConfig,
    IDistributedCache cache)
{
    public async IAsyncEnumerable<EmulatorQueue> GetQueues()
    {
        if (await cache.GetAsync(CacheKeyHelper.Queues) is { } queueBytes)
        {
            var queues = JsonSerializer.Deserialize<IEnumerable<EmulatorQueue>>(queueBytes);
            foreach (var queue in queues!)
            {
                yield return queue;
            }
        }
    }

    public async IAsyncEnumerable<EmulatorTopic> GetTopics()
    {
        if (await cache.GetAsync(CacheKeyHelper.Topics) is { } bytes)
        {
            var topics = JsonSerializer.Deserialize<IEnumerable<EmulatorTopic>>(bytes);
            foreach (var topic in topics!)
            {
                yield return topic;
            }
        }
    }

    public IEnumerable<EmulatorSubscription> GetSubscriptions() =>
        from emulatorNamespace in emulatorConfig.Value.UserConfig.Namespaces
        from queue in emulatorNamespace.Topics
        from subscription in queue.Subscriptions
        select new EmulatorSubscription(subscription.Name, queue.Name);
}

public record EmulatorQueue(string Namespace, string Name, int ActiveMessageCount, int DeadLetterCount);

public record EmulatorTopic(string Name, int ActiveMessageCount, int DeadLetterCount);

public record EmulatorSubscription(string Name, string TopicName);

public record ServiceBusMessageViewModel(long SequenceNumber, string MessageId, DateTimeOffset EnqueuedTime, string State, string BodySize, string LabelSubject, Dictionary<string, object> Properties, string Body);