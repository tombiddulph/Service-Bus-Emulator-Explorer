using System.Net;
using System.Net.Http.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ServiceBusEmulatorExplorer.Tests;

[NotInParallel]
public class PeekTests : TestBase
{
    private const string QueuePath = "/api/queues/peek-test/messages";
    private const string SubscriptionPath = "/api/topics/peek-topic/subscriptions/peek-sub/messages";

    [Test]
    [Arguments(QueuePath)]
    [Arguments(SubscriptionPath)]
    public async Task PeekReturnsBrokerFailureInsteadOfEmptySuccess(string path)
    {
        var broker = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        foreach (var (reason, expectedStatus) in new[]
                 {
                     (ServiceBusFailureReason.MessagingEntityNotFound, HttpStatusCode.NotFound),
                     (ServiceBusFailureReason.ServiceCommunicationProblem, HttpStatusCode.ServiceUnavailable),
                     (ServiceBusFailureReason.ServiceTimeout, HttpStatusCode.GatewayTimeout)
                 })
        {
            broker.PeekException = new ServiceBusException("Simulated broker failure", reason);
            var response = await Factory.CreateClient().GetAsync(path + "?mode=peek&state=active");
            await Assert.That(response.StatusCode).IsEqualTo(expectedStatus);
            await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            await Assert.That(problem!.Detail).IsNotEmpty();
            await Assert.That(problem.Extensions.ContainsKey("traceId")).IsTrue();
        }
    }

    [Test]
    [Arguments(QueuePath)]
    [Arguments(SubscriptionPath)]
    public async Task PeekReturnsTimeoutProblemForExpiredOperation(string path)
    {
        var broker = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        broker.PeekException = new OperationCanceledException("Simulated peek deadline");
        var response = await Factory.CreateClient().GetAsync(path + "?mode=peek&state=active");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.GatewayTimeout);
    }

    [Test]
    [Arguments(QueuePath)]
    [Arguments(SubscriptionPath)]
    public async Task PeekRejectsInvalidPaginationBeforeCreatingReceiver(string path)
    {
        var broker = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        foreach (var (query, field) in new[]
                 {
                     ("skip=-1", "skip"), ("skip=10001", "skip"),
                     ("take=0", "take"), ("take=-1", "take"), ("take=251", "take")
                 })
        {
            var response = await Factory.CreateClient().GetAsync(path + "?mode=peek&state=active&" + query);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
            await Assert.That(problem!.Errors.ContainsKey(field)).IsTrue();
        }
        await Assert.That(broker.CreatedReceivers).IsEmpty();
    }

    [Test]
    [Arguments(QueuePath, "peek-test")]
    [Arguments(SubscriptionPath, "peek-topic/Subscriptions/peek-sub")]
    public async Task PeekDistinguishesEmptyQueueFromPageBeyondAvailableMessages(string path, string entityPath)
    {
        var broker = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        var client = Factory.CreateClient();
        var empty = await client.GetFromJsonAsync<PagedMessages>(path + "?mode=peek&state=deadletter");
        await Assert.That(empty!.Items).IsEmpty();
        broker.AddDeadLetterMessage(entityPath, "peek-message", "body");
        var firstPage = await client.GetFromJsonAsync<PagedMessages>(path + "?mode=peek&state=deadletter&take=1");
        await Assert.That(firstPage!.Items.Single().MessageId).IsEqualTo("peek-message");
        var beyondEnd = await client.GetFromJsonAsync<PagedMessages>(path + "?mode=peek&state=deadletter&skip=2");
        await Assert.That(beyondEnd!.Items).IsEmpty();
        await Assert.That(beyondEnd.HasMore).IsFalse();
    }

    [Test]
    [Arguments(QueuePath, "peek-test", "active")]
    [Arguments(QueuePath, "peek-test", "deadletter")]
    [Arguments(SubscriptionPath, "peek-topic/Subscriptions/peek-sub", "active")]
    [Arguments(SubscriptionPath, "peek-topic/Subscriptions/peek-sub", "deadletter")]
    public async Task PeekSkipsAcrossPartialBrokerBatches(string path, string entityPath, string state)
    {
        var broker = (TestServiceBusClient)Factory.Services.GetRequiredService<ServiceBusClient>();
        broker.PeekBatchSize = 1;
        for (var i = 0; i < 4; i++)
        {
            if (state == "deadletter") broker.AddDeadLetterMessage(entityPath, $"message-{i}", "body");
            else broker.AddActiveMessage(entityPath, $"message-{i}", "body");
        }

        var page = await Factory.CreateClient().GetFromJsonAsync<PagedMessages>(
            path + $"?mode=peek&state={state}&skip=2&take=1");
        await Assert.That(page!.Items.Single().MessageId).IsEqualTo("message-2");
    }
}
