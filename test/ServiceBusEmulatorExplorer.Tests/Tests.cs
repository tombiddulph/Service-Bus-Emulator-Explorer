using System.Net;
using System.Net.Http.Json;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
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
    public async Task OpenApiDocumentCanBeGenerated()
    {
        var response = await Factory.CreateClient().GetAsync("/openapi/v1.json");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains("/api/queues");
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
        var result = await response.Content.ReadFromJsonAsync<ReplayDlqResult>();
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
    public async Task ReplayQueueDlqRejectsUnsupportedUserPropertyShape()
    {
        const string queueName = "replay-invalid-property-queue";
        const string messageId = "invalid-property-message";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        serviceBusClient.AddDeadLetterMessage(queueName, messageId, "original body");
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/deadletter/queue/{queueName}/replay", new
        {
            messageIds = new[] { messageId },
            userProperties = new Dictionary<string, object> { ["nested"] = new { a = 1 } }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(serviceBusClient.GetSentMessages(queueName)).IsEmpty();
        await Assert.That(serviceBusClient.GetDeadLetterMessages(queueName)).Count().IsEqualTo(1);
    }

    [Test]
    public async Task ReplayQueueDlqDoesNotCountMessageWhenCompleteIsCancelled()
    {
        const string queueName = "replay-cancelled-complete-queue";
        const string messageId = "cancelled-complete-message";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        serviceBusClient.AddDeadLetterMessage(queueName, messageId, "original body");
        serviceBusClient.FailCompleteForMessageId = messageId;
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/deadletter/queue/{queueName}/replay", new
        {
            messageIds = new[] { messageId },
            removeFromDlq = true
        });

        // Send succeeded but settlement was cancelled, so this is a partial result, not a 200.
        await Assert.That((int)response.StatusCode).IsEqualTo(207);
        var result = await response.Content.ReadFromJsonAsync<ReplayDlqResult>();
        await Assert.That(result).IsNotNull();
        // The count must only reflect messages that were actually completed, not just sent.
        await Assert.That(result!.Count).IsEqualTo(0);
        await Assert.That(result.IsPartial).IsTrue();
        await Assert.That(result.Outcomes).Count().IsEqualTo(1);
        await Assert.That(result.Outcomes[0].Sent).IsTrue();
        await Assert.That(result.Outcomes[0].RemovedFromDlq).IsFalse();
        await Assert.That(result.Outcomes[0].Error).IsNotEmpty();

        var replayedMessage = serviceBusClient.GetSentMessages(queueName).Single();
        await Assert.That(replayedMessage.MessageId).IsNotEqualTo(messageId);

        // The original message was abandoned (not completed), so it's still in the DLQ.
        await Assert.That(serviceBusClient.GetDeadLetterMessages(queueName)).Count().IsEqualTo(1);
    }

    [Test]
    public async Task ReplayQueueDlqHoldsEntityLockUntilIncompleteMessagesAreReleased()
    {
        const string queueName = "replay-concurrent-cleanup-queue";
        const string messageId = "blocked-abandon-message";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        serviceBusClient.AddDeadLetterMessage(queueName, messageId, "original body");
        serviceBusClient.FailCompleteForMessageId = messageId;
        serviceBusClient.BlockAbandonForMessageId = messageId;
        var client = Factory.CreateClient();

        var firstReplay = client.PostAsJsonAsync($"/api/deadletter/queue/{queueName}/replay", new
        {
            messageIds = new[] { messageId },
            removeFromDlq = true
        });
        await serviceBusClient.AbandonStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        serviceBusClient.FailCompleteForMessageId = null;
        var secondReplay = client.PostAsJsonAsync($"/api/deadletter/queue/{queueName}/replay", new
        {
            messageIds = new[] { messageId },
            removeFromDlq = true
        });

        await Task.Delay(100);
        await Assert.That(secondReplay.IsCompleted).IsFalse();

        serviceBusClient.AllowAbandon.TrySetResult();
        await Assert.That((int)(await firstReplay).StatusCode).IsEqualTo(207);
        await Assert.That((await secondReplay).StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(serviceBusClient.GetDeadLetterMessages(queueName)).IsEmpty();
    }

    [Test]
    public async Task ReplayQueueDlqRenewsLockDuringSlowSend()
    {
        const string queueName = "replay-lock-renewal-queue";
        const string messageId = "slow-send-message";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        serviceBusClient.SimulatedLockDuration = TimeSpan.FromMilliseconds(100);
        serviceBusClient.SendDelay = TimeSpan.FromMilliseconds(350);
        serviceBusClient.AddDeadLetterMessage(queueName, messageId, "original body");
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/deadletter/queue/{queueName}/replay", new
        {
            messageIds = new[] { messageId },
            removeFromDlq = true
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ReplayDlqResult>();
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.IsPartial).IsFalse();
        await Assert.That(result.Outcomes.Single().RemovedFromDlq).IsTrue();
        await Assert.That(serviceBusClient.RenewLockCallCount).IsGreaterThan(0);
        await Assert.That(serviceBusClient.GetDeadLetterMessages(queueName)).IsEmpty();
    }

    [Test]
    public async Task ReplayQueueDlqReportsRequestedMessageThatDisappearsAfterPeek()
    {
        const string queueName = "replay-disappearing-message-queue";
        const string messageId = "disappearing-message";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        serviceBusClient.AddDeadLetterMessage(queueName, messageId, "body");
        serviceBusClient.DisappearAfterPeekForMessageId = messageId;
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/deadletter/queue/{queueName}/replay", new
        {
            messageIds = new[] { messageId }
        });

        await Assert.That((int)response.StatusCode).IsEqualTo(207);
        var result = await response.Content.ReadFromJsonAsync<ReplayDlqResult>();
        await Assert.That(result!.IsPartial).IsTrue();
        await Assert.That(result.Outcomes).Count().IsEqualTo(1);
        await Assert.That(result.Outcomes.Single().MessageId).IsEqualTo(messageId);
        await Assert.That(result.Outcomes.Single().Sent).IsFalse();
        await Assert.That(result.Outcomes.Single().Error).IsNotEmpty();
    }

    [Test]
    public async Task ReplayQueueDlqReportsRequestedMessageLockedByAnotherReceiverAfterPeek()
    {
        const string queueName = "replay-externally-locked-message-queue";
        const string messageId = "externally-locked-message";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        serviceBusClient.AddDeadLetterMessage(queueName, messageId, "body");
        serviceBusClient.ExternallyLockAfterPeekForMessageId = messageId;
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/deadletter/queue/{queueName}/replay", new
        {
            messageIds = new[] { messageId }
        });

        await Assert.That((int)response.StatusCode).IsEqualTo(207);
        var result = await response.Content.ReadFromJsonAsync<ReplayDlqResult>();
        await Assert.That(result!.IsPartial).IsTrue();
        await Assert.That(result.Outcomes).Count().IsEqualTo(1);
        await Assert.That(result.Outcomes.Single().MessageId).IsEqualTo(messageId);
        await Assert.That(result.Outcomes.Single().Sent).IsFalse();
        await Assert.That(result.Outcomes.Single().Error).IsNotEmpty();
    }

    [Test]
    public async Task ReplayQueueDlqRenewsEarlyLocksWhileAcquiringLargePrefix()
    {
        const string queueName = "replay-large-prefix-renewal-queue";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        serviceBusClient.SimulatedLockDuration = TimeSpan.FromMilliseconds(100);
        serviceBusClient.DeadLetterReceiveDelayAfterFirstBatch = TimeSpan.FromMilliseconds(350);
        for (var i = 0; i < 101; i++)
            serviceBusClient.AddDeadLetterMessage(queueName, $"message-{i}", "body");
        var client = Factory.CreateClient();

        var replay = client.PostAsJsonAsync($"/api/deadletter/queue/{queueName}/replay", new
        {
            messageIds = new[] { "message-100" }
        });

        await Task.Delay(175);
        await Assert.That(replay.IsCompleted).IsFalse();
        await Assert.That(serviceBusClient.RenewLockCallCount).IsGreaterThan(0);
        await Assert.That((await replay).StatusCode).IsEqualTo(HttpStatusCode.OK);
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

        var result = await response.Content.ReadFromJsonAsync<DeleteDlqResult>();
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

        await Assert.That((int)response.StatusCode).IsEqualTo(207);

        var result = await response.Content.ReadFromJsonAsync<DeleteDlqResult>();
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Count).IsEqualTo(1);
        await Assert.That(result.NotFound).IsNotNull();
        await Assert.That(result.NotFound).Contains("this-id-does-not-exist");

        await Assert.That(serviceBusClient.GetDeadLetterMessages(entityPath)).IsEmpty();
    }

    [Test]
    public async Task DeleteAllQueueDlqReturnsConfirmedCount()
    {
        const string queueName = "delete-all-count-queue";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        serviceBusClient.AddDeadLetterMessage(queueName, "one", "body");
        serviceBusClient.AddDeadLetterMessage(queueName, "two", "body");

        var response = await Factory.CreateClient().PostAsync($"/api/deadletter/queue/{queueName}/delete", null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<DeleteDlqResult>();
        await Assert.That(result!.Count).IsEqualTo(2);
        await Assert.That(result.Status).IsEqualTo(DlqDeleteStatus.Completed);
        await Assert.That(serviceBusClient.GetDeadLetterMessages(queueName)).IsEmpty();
    }

    [Test]
    public async Task SelectedDeleteReportsSettlementFailureAndKeepsMessage()
    {
        const string queueName = "delete-settlement-failure-queue";
        const string messageId = "cannot-delete";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        serviceBusClient.AddDeadLetterMessage(queueName, messageId, "body");
        serviceBusClient.FailCompleteForMessageId = messageId;

        var response = await Factory.CreateClient().PostAsJsonAsync($"/api/deadletter/queue/{queueName}/delete",
            new { messageIds = new[] { messageId } });

        await Assert.That((int)response.StatusCode).IsEqualTo(207);
        var result = await response.Content.ReadFromJsonAsync<DeleteDlqResult>();
        await Assert.That(result!.Count).IsEqualTo(0);
        await Assert.That(result.IsPartial).IsTrue();
        await Assert.That(result.Outcomes.Single().Deleted).IsFalse();
        await Assert.That(result.Outcomes.Single().Error).IsNotEmpty();
        await Assert.That(serviceBusClient.GetDeadLetterMessages(queueName)).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SelectedDeleteHoldsLockUntilFailedMessageIsReleased()
    {
        const string queueName = "delete-cleanup-lock-queue";
        const string messageId = "blocked-delete-abandon";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        serviceBusClient.AddDeadLetterMessage(queueName, messageId, "body");
        serviceBusClient.FailCompleteForMessageId = messageId;
        serviceBusClient.BlockAbandonForMessageId = messageId;
        var client = Factory.CreateClient();

        var deletion = client.PostAsJsonAsync($"/api/deadletter/queue/{queueName}/delete", new { messageIds = new[] { messageId } });
        await serviceBusClient.AbandonStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        serviceBusClient.FailCompleteForMessageId = null;
        var replay = client.PostAsJsonAsync($"/api/deadletter/queue/{queueName}/replay", new { messageIds = new[] { messageId } });

        await Task.Delay(100);
        await Assert.That(replay.IsCompleted).IsFalse();
        serviceBusClient.AllowAbandon.TrySetResult();
        await Assert.That((int)(await deletion).StatusCode).IsEqualTo(207);
        await Assert.That((await replay).StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task SendQueueMessageDeliversBodyContentTypeAndUserProperties()
    {
        const string queueName = "send-message-queue";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/queues/{queueName}/messages", new
        {
            body = "{\"hello\":\"world\"}",
            contentType = "application/json",
            userProperties = new Dictionary<string, object> { ["source"] = "unit-test", ["retry"] = 3 }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var sentMessage = serviceBusClient.GetSentMessages(queueName).Single();
        await Assert.That(sentMessage.Body.ToString()).IsEqualTo("{\"hello\":\"world\"}");
        await Assert.That(sentMessage.ContentType).IsEqualTo("application/json");
        await Assert.That(sentMessage.ApplicationProperties["source"]).IsEqualTo("unit-test");
        await Assert.That(Convert.ToInt64(sentMessage.ApplicationProperties["retry"])).IsEqualTo(3L);

        // Confirm the body also round-trips through the peek endpoint the UI reads from.
        var peekResponse = await client.GetAsync($"/api/queues/{queueName}/messages?mode=peek&state=active");
        await Assert.That(peekResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var paged = await peekResponse.Content.ReadFromJsonAsync<PagedMessages>();
        await Assert.That(paged!.Items.Single().Body).IsEqualTo("{\"hello\":\"world\"}");
    }

    [Test]
    public async Task SendQueueMessageDeliversSessionId()
    {
        const string queueName = "send-message-session-queue";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/queues/{queueName}/messages", new
        {
            body = "session body",
            sessionId = "session-abc"
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var sentMessage = serviceBusClient.GetSentMessages(queueName).Single();
        await Assert.That(sentMessage.SessionId).IsEqualTo("session-abc");

        var peekResponse = await client.GetAsync($"/api/queues/{queueName}/messages?mode=peek&state=active");
        var paged = await peekResponse.Content.ReadFromJsonAsync<PagedMessages>();
        await Assert.That(paged!.Items.Single().SessionId).IsEqualTo("session-abc");
    }

    [Test]
    public async Task SendTopicMessageDeliversSessionId()
    {
        const string topicName = "send-message-session-topic";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        var client = Factory.CreateClient();

        await client.PostAsync("/api/topics", JsonContent.Create(new { name = topicName }));

        var response = await client.PostAsJsonAsync($"/api/topics/{topicName}/messages", new
        {
            body = "session body",
            sessionId = "session-xyz"
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(serviceBusClient.GetSentMessages(topicName).Single().SessionId).IsEqualTo("session-xyz");

        await client.DeleteAsync($"/api/topics/{topicName}");
    }

    [Test]
    public async Task PurgeQueueMessagesRemovesAllActiveMessagesOnly()
    {
        const string queueName = "purge-queue";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        serviceBusClient.AddDeadLetterMessage(queueName, "dlq-message", "dlq body");
        var client = Factory.CreateClient();

        await client.PostAsJsonAsync($"/api/queues/{queueName}/messages", new { body = "active-1" });
        await client.PostAsJsonAsync($"/api/queues/{queueName}/messages", new { body = "active-2" });

        var response = await client.PostAsync($"/api/queues/{queueName}/purge", null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PurgeResult>();
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Status).IsEqualTo(PurgeStatus.Completed);
        await Assert.That(result.RemovedCount).IsEqualTo(2);
        await Assert.That(serviceBusClient.GetSentMessages(queueName)).IsEmpty();
        // Purge only targets active messages, DLQ is untouched.
        await Assert.That(serviceBusClient.GetDeadLetterMessages(queueName)).Count().IsEqualTo(1);
    }

    [Test]
    public async Task PurgeSubscriptionMessagesRemovesPopulatedSubscription()
    {
        const string topicName = "purge-topic";
        const string subscriptionName = "purge-subscription";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        var client = Factory.CreateClient();

        await client.PostAsync("/api/topics", JsonContent.Create(new { name = topicName }));
        await client.PostAsync($"/api/topics/{topicName}/subscriptions", JsonContent.Create(new { name = subscriptionName }));
        var entityPath = $"{topicName}/Subscriptions/{subscriptionName}";
        serviceBusClient.AddActiveMessage(entityPath, "sub-1", "first");
        serviceBusClient.AddActiveMessage(entityPath, "sub-2", "second");

        var response = await client.PostAsync($"/api/topics/{topicName}/subscriptions/{subscriptionName}/purge", null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PurgeResult>();
        await Assert.That(result!.Status).IsEqualTo(PurgeStatus.Completed);
        await Assert.That(result.RemovedCount).IsEqualTo(2);
        await Assert.That(serviceBusClient.GetSentMessages(entityPath)).IsEmpty();

        await client.DeleteAsync($"/api/topics/{topicName}");
    }

    [Test]
    public async Task PurgeReturnsPartialFailureWithRemovedCount()
    {
        const string queueName = "purge-partial-failure";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        serviceBusClient.AddActiveMessage(queueName, "one", "one");
        serviceBusClient.AddActiveMessage(queueName, "two", "two");
        serviceBusClient.AddActiveMessage(queueName, "three", "three");
        serviceBusClient.ConfigurePurgeReceiver(queueName, batchSize: 1, failAfterCalls: 1);
        var client = Factory.CreateClient();

        var response = await client.PostAsync($"/api/queues/{queueName}/purge", null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadGateway);
        var result = await response.Content.ReadFromJsonAsync<PurgeResult>();
        await Assert.That(result!.Status).IsEqualTo(PurgeStatus.Failed);
        await Assert.That(result.RemovedCount).IsEqualTo(1);
        await Assert.That(serviceBusClient.GetSentMessages(queueName)).Count().IsEqualTo(2);
    }

    [Test]
    public async Task PurgeReportsPartialTimeout()
    {
        const string queueName = "purge-partial-timeout";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        serviceBusClient.AddActiveMessage(queueName, "one", "one");
        serviceBusClient.AddActiveMessage(queueName, "two", "two");
        serviceBusClient.ConfigurePurgeReceiver(queueName, batchSize: 1, delay: TimeSpan.FromSeconds(1), delayAfterCalls: 1);
        var endpointCache = Factory.Services.GetRequiredService<ServiceBusEndpointCache>();
        var receiver = endpointCache.GetReceiver(queueName, new ServiceBusReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete
        });

        var result = await Helpers.PurgeMessagesAsync(endpointCache, receiver, TimeSpan.FromMilliseconds(20));

        await Assert.That(result.Status).IsEqualTo(PurgeStatus.TimedOut);
        await Assert.That(result.RemovedCount).IsEqualTo(1);
        await Assert.That(serviceBusClient.GetSentMessages(queueName)).Count().IsEqualTo(1);
    }

    [Test]
    public async Task PurgeDoesNotReportCompletedWhenCancelledBetweenBatches()
    {
        const string queueName = "purge-between-batches-cancellation";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        serviceBusClient.AddActiveMessage(queueName, "one", "one");
        serviceBusClient.AddActiveMessage(queueName, "two", "two");
        serviceBusClient.ConfigurePurgeReceiver(queueName, batchSize: 1);
        using var cts = new CancellationTokenSource();
        serviceBusClient.ActiveBatchReceived = _ => cts.Cancel();
        var endpointCache = Factory.Services.GetRequiredService<ServiceBusEndpointCache>();
        var receiver = endpointCache.GetReceiver(queueName, new ServiceBusReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete
        });

        var result = await Helpers.PurgeMessagesAsync(endpointCache, receiver, cancellationToken: cts.Token);

        await Assert.That(result.Status).IsEqualTo(PurgeStatus.Failed);
        await Assert.That(result.RemovedCount).IsEqualTo(1);
        await Assert.That(serviceBusClient.GetSentMessages(queueName)).Count().IsEqualTo(1);
    }

    [Test]
    public async Task CreateSenderPreservesExistingDestinationMessages()
    {
        const string queueName = "existing-sender-state-queue";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        serviceBusClient.AddActiveMessage(queueName, "existing", "existing body");

        await serviceBusClient.CreateSender(queueName).SendMessageAsync(new ServiceBusMessage("new body"));

        await Assert.That(serviceBusClient.GetSentMessages(queueName)).Count().IsEqualTo(2);
    }

    [Test]
    public async Task PurgeRejectsSessionRequiredQueue()
    {
        const string queueName = "purge-session-queue";
        var adminClient = (TestServiceBusAdministrationClient)Factory.Services.GetRequiredService<ServiceBusAdministrationClient>();
        await adminClient.CreateQueueAsync(new CreateQueueOptions(queueName) { RequiresSession = true });
        var client = Factory.CreateClient();

        var response = await client.PostAsync($"/api/queues/{queueName}/purge", null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        var result = await response.Content.ReadFromJsonAsync<PurgeResult>();
        await Assert.That(result!.Status).IsEqualTo(PurgeStatus.SessionRequired);
        await Assert.That(result.Message).Contains("requires sessions");
    }

    [Test]
    public async Task SendQueueMessageRejectsUnsupportedUserPropertyShape()
    {
        const string queueName = "send-message-invalid-queue";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/queues/{queueName}/messages", new
        {
            body = "body",
            userProperties = new Dictionary<string, object> { ["nested"] = new { a = 1 } }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(serviceBusClient.GetSentMessages(queueName)).IsEmpty();
    }

    [Test]
    public async Task SendTopicMessageDeliversBodyContentTypeAndUserProperties()
    {
        const string topicName = "send-message-topic";
        var serviceBusClient = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        var client = Factory.CreateClient();

        await client.PostAsync("/api/topics", JsonContent.Create(new { name = topicName }));

        var response = await client.PostAsJsonAsync($"/api/topics/{topicName}/messages", new
        {
            body = "{\"hello\":\"topic\"}",
            contentType = "application/json",
            userProperties = new Dictionary<string, object> { ["source"] = "unit-test" }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var sentMessage = serviceBusClient.GetSentMessages(topicName).Single();
        await Assert.That(sentMessage.Body.ToString()).IsEqualTo("{\"hello\":\"topic\"}");
        await Assert.That(sentMessage.ContentType).IsEqualTo("application/json");
        await Assert.That(sentMessage.ApplicationProperties["source"]).IsEqualTo("unit-test");

        await client.DeleteAsync($"/api/topics/{topicName}");
    }

    [Test]
    public async Task SendTopicMessageReturnsBadRequestWhenTopicMissing()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/topics/missing-topic/messages", new { body = "body" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
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
