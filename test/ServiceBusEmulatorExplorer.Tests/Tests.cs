using System.Net;
using System.Net.Http.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;

namespace ServiceBusEmulatorExplorer.Tests;

[NotInParallel]
public class Tests : TestBase
{
    [Test]
    public async Task Basic()
    {
        var client = Factory.CreateClient();
        var response = await client.GetAsync("/health");

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
    }

    [Test]
    public async Task EnvironmentReturnsHostEnvironmentMetadata()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/api/environment");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var environment = await response.Content.ReadFromJsonAsync<EnvironmentInfo>();
        await Assert.That(environment).IsNotNull();
        await Assert.That(environment!.Name).IsNotEmpty();
    }

    [Test]
    public async Task ReplayQueueDlqReplaysSelectedMessageWithOverrides()
    {
        const string queueName = "replay-test-queue";
        const string messageId = "replay-message";
        const string skippedMessageId = "skipped-message";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        serviceBusClient.AddDeadLetterMessage(queueName, messageId, "original body", "application/json",
            new Dictionary<string, object> { ["source"] = "dlq" },
            partitionKey: "partition-key",
            sessionId: "session-id",
            timeToLive: TimeSpan.FromMinutes(3),
            correlationId: "correlation-id",
            subject: "subject",
            replyTo: "reply-to");
        serviceBusClient.AddDeadLetterMessage(queueName, skippedMessageId, "skipped body");
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/deadletter/queue/{queueName}/replay", new
        {
            messageIds = new[] { messageId },
            body = "replacement body",
            contentType = "text/plain",
            userProperties = new Dictionary<string, object> { ["source"] = "replay" }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CountResult>();
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Count).IsEqualTo(1);
        await Assert.That(result.NotFound).IsNull();

        var replayedMessage = serviceBusClient.GetSentMessages(queueName).Single();
        await Assert.That(replayedMessage.MessageId).IsNotEqualTo(messageId);
        await Assert.That(replayedMessage.MessageId).IsNotEmpty();
        await Assert.That(replayedMessage.ApplicationProperties["ReplayedFromMessageId"]).IsEqualTo(messageId);
        await Assert.That(replayedMessage.Body.ToString()).IsEqualTo("replacement body");
        await Assert.That(replayedMessage.ContentType).IsEqualTo("text/plain");
        await Assert.That(replayedMessage.PartitionKey).IsEqualTo("partition-key");
        await Assert.That(replayedMessage.SessionId).IsEqualTo("session-id");
        await Assert.That(replayedMessage.TimeToLive).IsEqualTo(TimeSpan.FromMinutes(3));
        await Assert.That(replayedMessage.CorrelationId).IsEqualTo("correlation-id");
        await Assert.That(replayedMessage.Subject).IsEqualTo("subject");
        await Assert.That(replayedMessage.ReplyTo).IsEqualTo("reply-to");
        await Assert.That(replayedMessage.ApplicationProperties["source"].ToString()).IsEqualTo("replay");

        var remainingMessages = serviceBusClient.GetDeadLetterMessages(queueName);
        await Assert.That(remainingMessages).Count().IsEqualTo(1);
        await Assert.That(remainingMessages[0].MessageId).IsEqualTo(skippedMessageId);
    }

    [Test]
    public async Task ReplaySubscriptionDlqReplaysAndRemovesMessage()
    {
        const string topicName = "replay-topic";
        const string subscriptionName = "replay-subscription";
        const string messageId = "subscription-replay-message";
        var entityPath = $"{topicName}/Subscriptions/{subscriptionName}";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        serviceBusClient.AddDeadLetterMessage(entityPath, messageId, "subscription body", "application/json");
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/deadletter/subscription/{topicName}/{subscriptionName}/replay",
            new { removeFromDlq = true });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CountResult>();
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Count).IsEqualTo(1);
        await Assert.That(result.NotFound).IsNull();

        var replayedMessage = serviceBusClient.GetSentMessages(topicName).Single();
        await Assert.That(replayedMessage.Body.ToString()).IsEqualTo("subscription body");
        await Assert.That(serviceBusClient.GetDeadLetterMessages(entityPath)).IsEmpty();
    }

    [Test]
    public async Task BulkDeleteQueueDlqRemovesOnlySelectedMessages()
    {
        const string queueName = "delete-test-queue";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        serviceBusClient.AddDeadLetterMessage(queueName, "keep-me", "keep body");
        serviceBusClient.AddDeadLetterMessage(queueName, "delete-me", "delete body");
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/deadletter/queue/{queueName}/delete", new
        {
            messageIds = new[] { "delete-me" }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<CountResult>();
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Count).IsEqualTo(1);
        await Assert.That(result.NotFound).IsNull();

        var remainingMessages = serviceBusClient.GetDeadLetterMessages(queueName);
        await Assert.That(remainingMessages).Count().IsEqualTo(1);
        await Assert.That(remainingMessages[0].MessageId).IsEqualTo("keep-me");
    }

    [Test]
    public async Task BulkDeleteSubscriptionDlqReportsNotFoundMessageIds()
    {
        const string topicName = "delete-notfound-topic";
        const string subscriptionName = "delete-notfound-sub";
        var entityPath = $"{topicName}/Subscriptions/{subscriptionName}";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        serviceBusClient.AddDeadLetterMessage(entityPath, "keep-me", "keep body");
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/deadletter/subscription/{topicName}/{subscriptionName}/delete", new
        {
            messageIds = new[] { "keep-me", "this-id-does-not-exist" }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<CountResult>();
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Count).IsEqualTo(1);
        await Assert.That(result.NotFound).IsNotNull();
        await Assert.That(result.NotFound).Contains("this-id-does-not-exist");

        await Assert.That(serviceBusClient.GetDeadLetterMessages(entityPath)).IsEmpty();
    }

    [Test]
    public async Task QueueCrudOperations()
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsync("/api/queues", JsonContent.Create(new { name = "test-queue" }));

        await Assert.That(response.IsSuccessStatusCode).IsTrue();

        var getResponse = await client.GetAsync("/api/queues/");
        await Assert.That(getResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var queues = await getResponse.Content.ReadFromJsonAsync<List<QueueInfo>>();

        await Assert.That(queues).Contains(x => x.Name == "test-queue");

        var deleteResponse = await client.DeleteAsync("/api/queues/test-queue");
        await Assert.That(deleteResponse.IsSuccessStatusCode).IsTrue();

        var getAfterDeleteResponse = await client.GetAsync("/api/queues/");
        await Assert.That(getAfterDeleteResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var queuesAfterDelete = await getAfterDeleteResponse.Content.ReadFromJsonAsync<List<QueueInfo>>();
        await Assert.That(queuesAfterDelete).DoesNotContain(x => x.Name == "test-queue");
    }

    [Test]
    public async Task TopicCrudOperations()
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsync("/api/topics", JsonContent.Create(new { name = "test-topic" }));
        await Assert.That(response.IsSuccessStatusCode).IsTrue();

        var getResponse = await client.GetAsync("/api/topics/");
        await Assert.That(getResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var topics = await getResponse.Content.ReadFromJsonAsync<List<TopicInfo>>();
        await Assert.That(topics).Contains(x => x.Name == "test-topic");
        var deleteResponse = await client.DeleteAsync("/api/topics/test-topic");
        await Assert.That(deleteResponse.IsSuccessStatusCode).IsTrue();
        var getAfterDeleteResponse = await client.GetAsync("/api/topics/");
        await Assert.That(getAfterDeleteResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var topicsAfterDelete = await getAfterDeleteResponse.Content.ReadFromJsonAsync<List<TopicInfo>>();
        await Assert.That(topicsAfterDelete).DoesNotContain(x => x.Name == "test-topic");
    }

    [Test]
    public async Task SubscriptionCrudOperations()
    {
        var client = Factory.CreateClient();

        // Create topic first
        var topicResponse = await client.PostAsync("/api/topics", JsonContent.Create(new { name = "sub-test-topic" }));
        await Assert.That(topicResponse.IsSuccessStatusCode).IsTrue();

        // Create subscription with custom properties
        var createSubResponse = await client.PostAsync("/api/topics/sub-test-topic/subscriptions",
            JsonContent.Create(new
            {
                name = "test-sub",
                maxDeliveryCount = 5,
                lockDuration = "00:02:00",
                defaultTtl = "01:00:00"
            }));
        await Assert.That(createSubResponse.IsSuccessStatusCode).IsTrue();

        // List subscriptions and verify properties
        var getResponse = await client.GetAsync("/api/topics/sub-test-topic/subscriptions");
        await Assert.That(getResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var subscriptions = await getResponse.Content.ReadFromJsonAsync<List<SubscriptionInfo>>();
        await Assert.That(subscriptions).IsNotNull();
        await Assert.That(subscriptions!.Count).IsEqualTo(1);

        var sub = subscriptions[0];
        await Assert.That(sub.Name).IsEqualTo("test-sub");
        await Assert.That(sub.MaxDeliveryCount).IsEqualTo(5);
        await Assert.That(sub.LockDuration).IsEqualTo(TimeSpan.FromMinutes(2).ToString());
        await Assert.That(sub.DefaultTtl).IsEqualTo(TimeSpan.FromHours(1).ToString());
        await Assert.That(sub.CreatedAt).IsNotNull();

        // Delete subscription
        var deleteResponse = await client.DeleteAsync("/api/topics/sub-test-topic/subscriptions/test-sub");
        await Assert.That(deleteResponse.IsSuccessStatusCode).IsTrue();

        // Verify deletion
        var getAfterDeleteResponse = await client.GetAsync("/api/topics/sub-test-topic/subscriptions");
        var subsAfterDelete = await getAfterDeleteResponse.Content.ReadFromJsonAsync<List<SubscriptionInfo>>();
        await Assert.That(subsAfterDelete).DoesNotContain(x => x.Name == "test-sub");

        // Cleanup topic
        await client.DeleteAsync("/api/topics/sub-test-topic");
    }
}