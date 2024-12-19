using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Options;
using OtelDemo.Shared;

namespace OtelDemo.Kiwis;

public class KiwisSubscriber(
    IAzureClientFactory<ServiceBusProcessor> processorFactory,
    IOptions<MessageSubscriberSettings> options,
    ILogger<MessageSubscriberBase> logger) : MessageSubscriberBase(processorFactory, options, logger)
{
    public override async Task HandleMessage(ProcessMessageEventArgs args, CancellationToken token)
    {

        if (args.Message.ApplicationProperties.TryGetValue(Constants.MessageType, out var value) && value?.ToString() is Constants.HealthCheck)
        {
            await args.CompleteMessageAsync(args.Message, token);
            logger.LogInformation("Health check message received");
            return;
        }

        using var activity = ActivityConfig.Source.CreateActivityFromMessage(args.Message, args.Message.Body.ToObjectFromJson<OtelDemoMessage>()!.UseLinks);
        activity?.AddEvent(new ActivityEvent("Processing complete"));
        await args.CompleteMessageAsync(args.Message, token);
    }
}