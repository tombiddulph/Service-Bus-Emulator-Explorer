using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Azure;
using Scalar.AspNetCore;
using ServiceBusEmulatorExplorer.Endpoints;
using ServiceBusEmulatorExplorer;
using ServiceBusEmulatorExplorer.Extensions;
using ConnectionInfo = ServiceBusEmulatorExplorer.ConnectionInfo;

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
var explorerOptions = builder.Configuration.GetSection("Explorer").Get<ExplorerOptions>() ?? new ExplorerOptions();
builder.Services.AddOptions<ExplorerOptions>().BindConfiguration("Explorer");

builder.Services
    .AddOpenApi()
    .AddEndpointsApiExplorer()
    .AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    })
    .AddExceptionHandler<ServiceBusExceptionHandler>();

builder.AddExplorerOpenTelemetry();

builder.Services.AddHealthChecks();
builder.Services.AddSingleton(serviceBusConfig);
builder.Services.AddSingleton<IAmqpTransportProbe, AmqpTransportProbe>();
builder.Services.AddSingleton<ExplorerReadiness>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
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
builder.Services.AddOptions<DlqOperationOptions>();


builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(explorerOptions.AllowedOrigins)
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
app.Use(async (context, next) =>
{
    var options = context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<ExplorerOptions>>().Value;
    if (options.ReadOnly && context.Request.Path.StartsWithSegments("/api") &&
        !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) &&
        !HttpMethods.IsOptions(context.Request.Method))
    {
        await Results.Problem("This explorer is configured as read-only.", statusCode: 403, title: "Read-only mode").ExecuteAsync(context);
        return;
    }
    await next(context);
});
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

var api = app.MapGroup("/api");
api.MapGet("/environment", (IHostEnvironment environment, Microsoft.Extensions.Options.IOptions<ExplorerOptions> options) =>
    Results.Ok(new EnvironmentInfo(environment.EnvironmentName, options.Value.ReadOnly, Math.Clamp(options.Value.RefreshIntervalMs, 1000, 60000))))
    .Produces<EnvironmentInfo>();
api.AddEndpointFilter(async (context, next) =>
{
    var result = await next(context);
    if (!HttpMethods.IsGet(context.HttpContext.Request.Method))
        context.HttpContext.RequestServices.GetRequiredService<ServiceBusEndpointCache>().InvalidateCounts();
    var operation = result switch
    {
        IValueHttpResult { Value: ReplayDlqResult replay } => ("replay", replay.IsPartial ? "partial" : "completed"),
        IValueHttpResult { Value: DeleteDlqResult delete } => ("delete", delete.Status.ToString()),
        IValueHttpResult { Value: PurgeResult purge } => ("purge", purge.Status.ToString()),
        _ => ((string, string)?)null
    };
    if (operation.HasValue) ExplorerMetrics.BulkOperations.Add(1,
        new KeyValuePair<string, object?>("operation", operation.Value.Item1),
        new KeyValuePair<string, object?>("outcome", operation.Value.Item2));
    return result;
});
api.MapGet("/connection", async (ExplorerReadiness readiness, CancellationToken token) =>
    Results.Ok(await readiness.CheckAsync(token))).Produces<ConnectionInfo>();
api
    .MapQueueEndpoints()
    .MapTopicEndpoints()
    .MapSubscriptionEndpoints()
    .MapDeadLetterEndpoints();

app.UseHealthChecks("/health");
app.UseHealthChecks("/health/live");
app.MapGet("/health/ready", async (ExplorerReadiness readiness, CancellationToken token) =>
{
    var status = await readiness.CheckAsync(token);
    return Results.Json(status, AppJsonContext.Default.ConnectionInfo, statusCode: status.Ready ? 200 : 503);
}).Produces<ConnectionInfo>().Produces<ConnectionInfo>(503);

if (app.Environment.IsDevelopment())
{
    app.UseWhen(context =>
        !context.Request.Path.StartsWithSegments("/api")
        && !context.Request.Path.StartsWithSegments("/health")
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
