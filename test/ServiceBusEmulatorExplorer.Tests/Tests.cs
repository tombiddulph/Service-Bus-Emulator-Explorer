using System.Net;
using System.Net.Http.Json;

namespace ServiceBusEmulatorExplorer.Tests;

public class Tests : TestBase
{
    [Test]
    public async Task Basic()
    {
        var client = Factory.CreateClient();
        var response = await client.GetAsync("/health");

        await Assert.That(response.IsSuccessStatusCode).IsEqualTo(true);
    }

    [Test]
    public async Task QueueCrudOperations()
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsync("/api/queues", JsonContent.Create(new { name = "test-queue" }));

        await Assert.That(response.IsSuccessStatusCode).IsEqualTo(true);

        var getResponse = await client.GetAsync("/api/queues/");
        await Assert.That(getResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var queues = await getResponse.Content.ReadFromJsonAsync<List<QueueInfo>>();

        await Assert.That(queues).Contains(x => x.Name == "test-queue");

        var deleteResponse = await client.DeleteAsync("/api/queues/test-queue");
        await Assert.That(deleteResponse.IsSuccessStatusCode).IsEqualTo(true);

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
        await Assert.That(response.IsSuccessStatusCode).IsEqualTo(true);

        var getResponse = await client.GetAsync("/api/topics/");
        await Assert.That(getResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var topics = await getResponse.Content.ReadFromJsonAsync<List<TopicInfo>>();
        await Assert.That(topics).Contains(x => x.Name == "test-topic");
        var deleteResponse = await client.DeleteAsync("/api/topics/test-topic");
        await Assert.That(deleteResponse.IsSuccessStatusCode).IsEqualTo(true);
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
        await Assert.That(topicResponse.IsSuccessStatusCode).IsEqualTo(true);

        // Create subscription with custom properties
        var createSubResponse = await client.PostAsync("/api/topics/sub-test-topic/subscriptions",
            JsonContent.Create(new
            {
                name = "test-sub",
                maxDeliveryCount = 5,
                lockDuration = "00:02:00",
                defaultTtl = "01:00:00"
            }));
        await Assert.That(createSubResponse.IsSuccessStatusCode).IsEqualTo(true);

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
        await Assert.That(deleteResponse.IsSuccessStatusCode).IsEqualTo(true);

        // Verify deletion
        var getAfterDeleteResponse = await client.GetAsync("/api/topics/sub-test-topic/subscriptions");
        var subsAfterDelete = await getAfterDeleteResponse.Content.ReadFromJsonAsync<List<SubscriptionInfo>>();
        await Assert.That(subsAfterDelete).DoesNotContain(x => x.Name == "test-sub");

        // Cleanup topic
        await client.DeleteAsync("/api/topics/sub-test-topic");
    }
}