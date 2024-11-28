using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Options;
using MudBlazor.Extensions;
using MudBlazor.Services;
using SbExplorer.Mud;
using SbExplorer.Mud.Components;
using SbExplorer.Mud.Config;
using SbExplorer.Mud.Services;

var builder = WebApplication.CreateBuilder(args);
// const string connectionString =
//     "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
//
//
// const string configPath = "service-bus-config.json";


var redisConfig = new RedisConfig();
builder.Configuration.GetSection(RedisConfig.SectionName).Bind(redisConfig);

var serviceBusConfig = new ServiceBusConfig();
builder.Configuration.GetSection(ServiceBusConfig.SectionName).Bind(serviceBusConfig);
builder.Services.AddSingleton(Options.Create(serviceBusConfig));

if (!File.Exists(serviceBusConfig.EmulatorConfigFilePath))
{
    throw new FileNotFoundException("Service bus config file not found", serviceBusConfig.EmulatorConfigFilePath);
}

builder.Logging.AddConsole();
builder.Services.AddOptions();
builder.Services.AddMemoryCache();


builder.Services.AddStackExchangeRedisCache(opts =>
{
    opts.Configuration = redisConfig.ConnectionString;
    opts.InstanceName = redisConfig.InstanceName;
});

builder.Services.AddHostedService<ServiceBusUpdater>();
builder.Services.AddSingleton<ServiceBusAdmin>();

try
{
    builder.Services.AddSingleton(Options.Create(EmulatorConfig.FromJson(File.ReadAllText(serviceBusConfig.EmulatorConfigFilePath))));
}
catch (Exception e)
{
    Console.Error.WriteLine("Error reading config file");
    Console.Error.WriteLine(e);
    throw;
}

builder.Services.AddMudServices();
builder.Services.AddMudExtensions();
builder.Services.AddAzureClients(opts =>
{
    opts.AddClient<ServiceBusClient, ServiceBusClientOptions>((options, _, _) =>
        new CachingServiceBusClient(serviceBusConfig.ConnectionString, options));
});
// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();


app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();