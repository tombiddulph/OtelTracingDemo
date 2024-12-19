using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Options;
using OtelDemo.Shared;

namespace OtelDemo.Apples;

public class ApiTopicSubscriber(IAzureClientFactory<ServiceBusSender> senderFactory, IAzureClientFactory<ServiceBusProcessor> processorFactory, IOptions<MessageSubscriberSettings> options, ILogger<MessageSubscriberBase> logger) : MessageSubscriberBase(senderFactory, processorFactory, options, logger)
{
    public override async Task HandleMessage(ProcessMessageEventArgs args, CancellationToken token)
    {
        var otelDemoMessage = args.Message.Body.ToObjectFromJson<OtelDemoMessage>()!;

        using var activity = ActivityConfig.Source.CreateActivityFromMessage(args.Message, otelDemoMessage.UseLinks);

        activity?.AddEvent(new ActivityEvent("Processing message"));

        var newMessage = new ServiceBusMessage(args.Message.Body)
        {
            ContentType = "application/json"
        };
        newMessage.ApplicationProperties.Add(Constants.DestinationQueueName, "peaches");
        newMessage.InjectContext();
        await Sender!.SendMessageAsync(newMessage, token);
        await args.CompleteMessageAsync(args.Message, token);
    }
}
