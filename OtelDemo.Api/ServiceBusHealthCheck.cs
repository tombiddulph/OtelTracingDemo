using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OtelDemo.Shared;

namespace OtelDemo.Api;

public class ServiceBusHealthCheck(
    IAzureClientFactory<ServiceBusSender> senderFactory,
    IOptions<HealthCheckSettings> healthCheckSettings)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivityConfig.Source.StartActivity("Service Bus Health Check");

        try
        {
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

            var sender = senderFactory.CreateClient(healthCheckSettings.Value.SenderName);
            var message = new ServiceBusMessage("Health check");
            message.ApplicationProperties.Add(Constants.MessageType, Constants.HealthCheck);
            var senderTask = sender.SendMessageAsync(message, cancellationToken);

            var result = await Task.WhenAny(senderTask, timeoutTask);

            if (result == timeoutTask)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                activity?.AddEvent(new ActivityEvent("Service Bus is unhealthy"));
                return HealthCheckResult.Unhealthy("Service Bus is unhealthy");
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.AddEvent(new ActivityEvent("Service Bus is healthy"));
            return HealthCheckResult.Healthy();
        }
        catch (Exception e)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.AddException(e);
            return HealthCheckResult.Unhealthy("Service Bus is unhealthy", e);
        }
    }
}