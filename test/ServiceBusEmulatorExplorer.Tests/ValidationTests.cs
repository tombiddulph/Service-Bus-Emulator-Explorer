using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;

namespace ServiceBusEmulatorExplorer.Tests;

[NotInParallel]
public class ValidationTests : TestBase
{
    [Test]
    [Arguments("/api/queues")]
    [Arguments("/api/topics/test-topic/subscriptions")]
    public async Task CreationRejectsInvalidFields(string path)
    {
        var response = await Factory.CreateClient().PostAsJsonAsync(path, new
        {
            name = "bad/name", maxDeliveryCount = 0, lockDuration = "00:06:00", defaultTtl = "invalid"
        });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        foreach (var field in new[] { "name", "maxDeliveryCount", "lockDuration", "defaultTtl" })
            await Assert.That(problem!.Errors.ContainsKey(field)).IsTrue();
    }

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("bad/name")]
    public async Task TopicCreationRejectsInvalidName(string name)
    {
        var response = await Factory.CreateClient().PostAsJsonAsync("/api/topics", new { name });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PeekRejectsUndefinedEnumValues()
    {
        var response = await Factory.CreateClient().GetAsync("/api/queues/test/messages?mode=peek&state=99");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    [Arguments("00:00:00")]
    [Arguments("01:00:01")]
    public async Task CreationRejectsTtlOutsideEmulatorLimits(string ttl)
    {
        var response = await Factory.CreateClient().PostAsJsonAsync("/api/queues", new { name = "ttl-test", defaultTtl = ttl });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        await Assert.That(problem!.Errors.ContainsKey("defaultTtl")).IsTrue();
    }

    [Test]
    public async Task OmittedQueueOptionsUseEmulatorCompatibleDefaults()
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/queues", new { name = "default-options" });
        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        var queues = await client.GetFromJsonAsync<List<QueueInfo>>("/api/queues");
        var queue = queues!.Single(item => item.Name == "default-options");
        await Assert.That(queue.DefaultTtl).IsEqualTo("01:00:00");
        await Assert.That(queue.LockDuration).IsEqualTo("00:01:00");
    }
}
