using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TUnit.AspNetCore;

namespace ServiceBusEmulatorExplorer.Tests;

public class WebApplicationFactory : TestWebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ServiceBusAdministrationClient,TestServiceBusAdministrationClient>();
            services.AddSingleton<ServiceBusClient,TestServiceBusClient>();
            services.AddSingleton<IAmqpTransportProbe, TestAmqpProbe>();
        });
    }
}

public abstract class TestBase : WebApplicationTest<WebApplicationFactory, Program>;

public class TestAmqpProbe : IAmqpTransportProbe
{
    public bool Available { get; set; } = true;
    public Task<bool> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(Available);
}
