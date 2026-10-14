using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace ServiceBusEmulatorExplorer.Tests;

[NotInParallel]
public class ReadinessTests : TestBase
{
    [Test]
    public async Task HealthyDependenciesReportReady()
    {
        var response = await Factory.CreateClient().GetAsync("/health/ready");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var status = await response.Content.ReadFromJsonAsync<ConnectionInfo>();
        await Assert.That(status!.Ready).IsTrue();
    }

    [Test]
    public async Task DependencyFailureDoesNotAffectLiveness()
    {
        ((TestAmqpProbe)Factory.Services.GetRequiredService<IAmqpTransportProbe>()).Available = false;
        var client = Factory.CreateClient();
        await Assert.That((await client.GetAsync("/health/ready")).StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That((await client.GetAsync("/health/live")).StatusCode).IsEqualTo(HttpStatusCode.OK);
        var status = await client.GetFromJsonAsync<ConnectionInfo>("/api/connection");
        await Assert.That(status!.AdministrationAvailable).IsTrue();
        await Assert.That(status.AmqpTransportAvailable).IsFalse();
    }
}
