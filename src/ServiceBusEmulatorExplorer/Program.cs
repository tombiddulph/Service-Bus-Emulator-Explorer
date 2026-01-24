using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Azure;
using Scalar.AspNetCore;
using ServiceBusEmulatorExplorer.Endpoints;
using ServiceBusEmulatorExplorer;
using ServiceBusEmulatorExplorer.Extensions;

#if DEBUG
var builder = WebApplication.CreateBuilder(args);
#else
var builder = WebApplication.CreateSlimBuilder(args);
#endif


builder
    .Configuration.AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

var serviceBusConfig = builder.Configuration.GetSection(ServiceBusConfig.SectionName).Get<ServiceBusConfig>() ??
                       throw new InvalidOperationException("ServiceBusConfig section is missing");

builder.Services
    .AddOpenApi()
    .AddEndpointsApiExplorer()
    .AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    })
    .AddExceptionHandler<ServiceBusExceptionHandler>();

builder.AddOpenTelemetry();

builder.Services.AddHealthChecks();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<MessageState>(JsonNamingPolicy.CamelCase));
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<EntityStatus>(JsonNamingPolicy.CamelCase));
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<PeekMode>(JsonNamingPolicy.CamelCase));
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
});


builder.Services.AddAzureClients(clientBuilder =>
{
    clientBuilder.AddServiceBusClient(serviceBusConfig.ConnectionString);
    clientBuilder.AddServiceBusAdministrationClient(serviceBusConfig.AdministrationConnectionString);
});

builder.Services.AddSpaStaticFiles(options => { options.RootPath = "wwwroot"; });

builder.Services.AddSingleton<ServiceBusEndpointCache>();


builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});


var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
if (!app.Environment.IsDevelopment())
{
    app.UseSpaStaticFiles();
}

app.UseCors("AllowFrontend");
app.MapOpenApi();
app.MapScalarApiReference();

app.UseExceptionHandler();
app.UseStatusCodePages(async statusCodeContext =>
{
    var httpContext = statusCodeContext.HttpContext;
    if (httpContext.Response.HasStarted)
    {
        return;
    }

    var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
    var statusCode = httpContext.Response.StatusCode;
    var problemDetails = new ProblemDetails
    {
        Status = statusCode,
        Title = ReasonPhrases.GetReasonPhrase(statusCode)
    };

    await problemDetailsService.WriteAsync(new ProblemDetailsContext
    {
        HttpContext = httpContext,
        ProblemDetails = problemDetails
    });
});

app.UseHttpsRedirection();

app.MapGroup("/api")
    .MapQueueEndpoints()
    .MapTopicEndpoints()
    .MapSubscriptionEndpoints()
    .MapDeadLetterEndpoints();

app.UseHealthChecks("/health");

if (app.Environment.IsDevelopment())
{
    app.UseWhen(context =>
        !context.Request.Path.StartsWithSegments("/api")
        && !context.Request.Path.StartsWithSegments("/scalar")
        && !context.Request.Path.StartsWithSegments("/openapi"), spaApp =>
    {
        spaApp.UseSpa(spa =>
        {
            spa.Options.SourcePath = "../app/sb-explorer-ui";
            var proxyUrl = builder.Configuration["SpaProxyServerUrl"] ?? "http://localhost:5173";
            spa.UseProxyToSpaDevelopmentServer(proxyUrl);
        });
    });
}
else
{
    app.MapFallbackToFile("index.html");
}

app.Run();