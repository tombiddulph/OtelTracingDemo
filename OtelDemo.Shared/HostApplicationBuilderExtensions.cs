using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace OtelDemo.Shared;

public static class ServiceCollectionExtensions
{
    public static void AddServiceBus(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAzureClients(clientsBuilder => { clientsBuilder.AddServiceBusClient(configuration); });
    }

    public static void AddOpenTelemetry(this IServiceCollection services, string serviceName)
    {
        services.AddOpenTelemetry().ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(builder => builder.AddSource(serviceName).AddOtlpExporter())
            .WithMetrics(builder => builder.AddOtlpExporter())
            .WithLogging(builder => builder.AddOtlpExporter());
    }
}