using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using SbExplorer.Mud.Config;
using SbExplorer.Mud.Helpers;

namespace SbExplorer.Mud.Services;

public class ServiceBusUpdater(
    ServiceBusClient client,
    IOptions<EmulatorConfig> emulatorConfig,
    IOptions<ServiceBusConfig> serviceBusConfig,
    IDistributedCache cache,
    ILogger<ServiceBusUpdater> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                logger.LogInformation("Updating queues and topics");
                await Task.WhenAll(UpdateQueues(), UpdateTopics());
                await Task.Delay(TimeSpan.FromMilliseconds(serviceBusConfig.Value.RefreshIntervalMs), stoppingToken);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error updating queues and topics");
            }
        }
    }


    private async Task UpdateTopics()
    {
        List<EmulatorTopic> result = [];
        foreach (var emulatorNamespace in emulatorConfig.Value.UserConfig.Namespaces)
        {
            foreach (var topic in emulatorNamespace.Topics)
            {
                var receiver = client.CreateReceiver(topic.Name,
                    new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.PeekLock });
                var deadLetterReceiver = client.CreateReceiver(topic.Name,
                    new ServiceBusReceiverOptions
                    { ReceiveMode = ServiceBusReceiveMode.PeekLock, SubQueue = SubQueue.DeadLetter });
                var activeMessages = await receiver.PeekMessagesAsync(1000, long.MinValue);
                var deadLetterMessages = (await deadLetterReceiver.PeekMessagesAsync(1000, long.MinValue));
                var emulatorTopic = new EmulatorTopic(topic.Name, activeMessages.Count, deadLetterMessages.Count);
                result.Add(emulatorTopic);
            }
        }

        await cache.SetAsync(CacheKeyHelper.Topics, JsonSerializer.SerializeToUtf8Bytes(result));
    }

    private async Task UpdateQueues()
    {
        List<EmulatorQueue> result = [];
        foreach (var emulatorNamespace in emulatorConfig.Value.UserConfig.Namespaces)
        {
            foreach (var queue in emulatorNamespace.Queues)
            {
                var receiver = client.CreateReceiver(queue.Name,
                    new ServiceBusReceiverOptions
                    { ReceiveMode = ServiceBusReceiveMode.PeekLock, SubQueue = SubQueue.None });
                var deadLetterReceiver = client.CreateReceiver(queue.Name,
                    new ServiceBusReceiverOptions
                    { ReceiveMode = ServiceBusReceiveMode.PeekLock, SubQueue = SubQueue.DeadLetter });

                var activeMessages = await receiver.PeekMessagesAsync(1000, long.MinValue);
                var deadLetterMessages = (await deadLetterReceiver.PeekMessagesAsync(1000, long.MinValue));


                await cache.SetAsync(CacheKeyHelper.QueueKey(emulatorNamespace.Name, queue.Name),
                    JsonSerializer.SerializeToUtf8Bytes(activeMessages.Select(CreateViewModel)));

                await cache.SetAsync(CacheKeyHelper.DeadLetterQueueKey(emulatorNamespace.Name, queue.Name),
                    JsonSerializer.SerializeToUtf8Bytes(deadLetterMessages.Select(CreateViewModel)));
                var emulatorQueue = new EmulatorQueue(emulatorNamespace.Name, queue.Name, activeMessages.Count,
                    deadLetterMessages.Count);
                result.Add(emulatorQueue);
            }
        }

        await cache.SetAsync(CacheKeyHelper.Queues, JsonSerializer.SerializeToUtf8Bytes(result));
    }

    private static ServiceBusMessageViewModel CreateViewModel(ServiceBusReceivedMessage m) =>
        new(m.SequenceNumber, m.MessageId, m.EnqueuedTime, m.State.ToString(),
            Encoding.Default.GetByteCount(m.Body.ToString()).ToString(), m.Subject,
            m.ApplicationProperties.ToDictionary(), m.Body.ToString());
}