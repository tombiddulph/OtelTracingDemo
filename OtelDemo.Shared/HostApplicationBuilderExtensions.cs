using Azure.Messaging.ServiceBus;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace OtelDemo.Shared;

public static class HostApplicationBuilderExtensions
{
    public static void AddServiceBus(
        this IHostApplicationBuilder hostBuilder,
        string? processorName = null,
        string? processorSubscriptionName = null,
        params string[] senderNames)
    {
        hostBuilder.Services.AddAzureClients(clientsBuilder =>
        {
            clientsBuilder.AddServiceBusClient(hostBuilder.Configuration["ServiceBus:Namespace"]);


            if (!string.IsNullOrEmpty(processorName) && !string.IsNullOrEmpty(processorSubscriptionName))
            {
                clientsBuilder.AddClient<ServiceBusProcessor, ServiceBusProcessorOptions>((opts, _, provider) =>
                {
                    opts.MaxConcurrentCalls = 50;
                    return provider.GetRequiredService<ServiceBusClient>()
                        .CreateProcessor(processorName, processorSubscriptionName);
                }).WithName(processorName);
            }
            else if (!string.IsNullOrEmpty(processorName))
            {
                clientsBuilder.AddClient<ServiceBusProcessor, ServiceBusProcessorOptions>((options, _, provider) =>
                {
                    options.MaxConcurrentCalls = 50;
                    return provider.GetRequiredService<ServiceBusClient>().CreateProcessor(processorName);
                }).WithName(processorName);
            }

            foreach (var senderName in senderNames)
            {
                clientsBuilder.AddClient<ServiceBusSender, ServiceBusSenderOptions>((_, _, provider) =>
                    provider.GetRequiredService<ServiceBusClient>().CreateSender(senderName)).WithName(senderName);
            }
        });
    }


    public static void AddOpenTelemetry(this IHostApplicationBuilder hostBuilder, string serviceName,
        Action<TracerProviderBuilder>? customTracing = null)
    {
        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
        {
            // hostBuilder.Logging.ClearProviders();
        }


        hostBuilder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(builder =>
            {
                builder.AddSource(serviceName);
                AppContext.SetSwitch("System.Diagnostics.Tracing.EventSource", true);
                builder.AddSource("Azure.*");
                builder.AddHttpClientInstrumentation();
                customTracing?.Invoke(builder);
                builder.SetSampler(new AlwaysOnSampler());
                builder.SetErrorStatusOnException();


                if (Environment.GetEnvironmentVariable("APPINSIGHTS_CONNECTIONSTRING") is { } connectionString && !string.IsNullOrEmpty(connectionString))
                {
                    builder.AddAzureMonitorTraceExporter(ops =>
                    {
                        ops.ConnectionString = connectionString;
                    });
                }

            })
            .WithMetrics(builder =>
            {
                if (Environment.GetEnvironmentVariable("APPINSIGHTS_CONNECTIONSTRING") is { } connectionString && !string.IsNullOrEmpty(connectionString))
                {
                    builder.AddAzureMonitorMetricExporter(ops =>
                    {
                        ops.ConnectionString = connectionString;
                    });
                }
            })
            .WithLogging(builder => { })
            .UseOtlpExporter(OtlpExportProtocol.Grpc, new Uri("http://oteldemo.dashboard:18889"));
    }
}