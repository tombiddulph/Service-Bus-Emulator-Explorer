using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using TUnit.AspNetCore;

namespace ServiceBusEmulatorExplorer.Tests;

public class ReadOnlyFactory : WebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Explorer:ReadOnly"] = "true" }));
    }
}

[NotInParallel]
public class SafetyTests : WebApplicationTest<ReadOnlyFactory, Program>
{
    [Test]
    public async Task ReadOnlyRejectsAllMutationMethodsBeforeBindingRequests()
    {
        var client = Factory.CreateClient();
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            using var request = new HttpRequestMessage(method, "/api/queues/anything");
            var response = await client.SendAsync(request);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        }
        var environment = await client.GetFromJsonAsync<EnvironmentInfo>("/api/environment");
        await Assert.That(environment!.ReadOnly).IsTrue();
        await Assert.That((await client.GetAsync("/api/queues")).StatusCode).IsEqualTo(HttpStatusCode.OK);
    }
}

[NotInParallel]
public class CorsTests : TestBase
{
    [Test]
    [Arguments("http://localhost:5173", true)]
    [Arguments("https://untrusted.example", false)]
    public async Task CorsOnlyAllowsConfiguredOrigins(string origin, bool allowed)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/queues");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        var response = await Factory.CreateClient().SendAsync(request);
        await Assert.That(response.Headers.Contains("Access-Control-Allow-Origin")).IsEqualTo(allowed);
    }
}
