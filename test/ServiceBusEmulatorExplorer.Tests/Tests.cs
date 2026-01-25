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
}