using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ServiceBusEmulatorExplorer.Extensions;

public static class BuilderExtensions
{
    public static IHostApplicationBuilder AddOpenTelemetry(this IHostApplicationBuilder builder)
    {
        var resourceBuilder = ResourceBuilder
            .CreateDefault()
            .AddService(serviceName: builder.Environment.ApplicationName,
                serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0")
            .AddAttributes([new KeyValuePair<string, object>("deployment.environment", builder.Environment.EnvironmentName)]);

        var otlpConfig = builder.Configuration.GetSection(OtlpConfig.SectionName).Get<OtlpConfig>() ??
                         throw new InvalidOperationException("OtlpConfig section is missing");
        
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.SetResourceBuilder(resourceBuilder);
            logging.IncludeScopes = true;
            logging.ParseStateValues = true;


            if (!string.IsNullOrEmpty(otlpConfig.Endpoint))
            {
                logging.AddOtlpExporter(opts =>
                {
                    opts.Endpoint = new Uri($"{otlpConfig.Endpoint}/v1/logs");
                    opts.Protocol = OtlpExportProtocol.HttpProtobuf;
                });
            }

            if (otlpConfig.EnableConsoleExporter)
            {
                logging.AddConsoleExporter();
            }
        });

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.SetResourceBuilder(resourceBuilder);
                tracing.SetSampler(new ParentBasedSampler(new AlwaysOnSampler()));
                tracing.AddAspNetCoreInstrumentation();
                tracing.AddHttpClientInstrumentation();

                if (!string.IsNullOrEmpty(otlpConfig.Endpoint))
                {
                    tracing.AddOtlpExporter(opts =>
                    {
                        opts.Endpoint = new Uri($"{otlpConfig.Endpoint}/v1/traces");
                        opts.Protocol = OtlpExportProtocol.HttpProtobuf;
                    });
                }

                if (otlpConfig.EnableConsoleExporter)
                {
                    tracing.AddConsoleExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.SetResourceBuilder(resourceBuilder);
                metrics.AddAspNetCoreInstrumentation();
                metrics.AddRuntimeInstrumentation();
                if (!string.IsNullOrEmpty(otlpConfig.Endpoint))
                {
                    metrics.AddOtlpExporter(opts =>
                    {
                        opts.Endpoint = new Uri($"{otlpConfig.Endpoint}/v1/metrics");
                        opts.Protocol = OtlpExportProtocol.HttpProtobuf;
                    });
                }

                if (otlpConfig.EnableConsoleExporter)
                {
                    metrics.AddConsoleExporter();
                }
            });
        
        return builder;
    }
}