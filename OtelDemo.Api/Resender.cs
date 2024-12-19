using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Options;
using OtelDemo.Shared;

namespace OtelDemo.Api;

public class Resender : MessageSubscriberBase
{

    public Resender(IAzureClientFactory<ServiceBusSender> senderFactory,
        IAzureClientFactory<ServiceBusProcessor> processorFactory,
        IOptions<MessageSubscriberSettings> options,
        IOptions<DestinationQueueSettings> destinationQueueSettings,
        ILogger<MessageSubscriberBase> logger) : base(senderFactory, processorFactory, options, logger)
    {

        foreach (var destinationQueue in destinationQueueSettings.Value.Queues)
        {
            _destinationQueues.Add(destinationQueue.Key, senderFactory.CreateClient(destinationQueue.Value));
        }
    }

    private readonly Dictionary<string, ServiceBusSender> _destinationQueues = new(StringComparer.OrdinalIgnoreCase);

    public override async Task HandleMessage(ProcessMessageEventArgs args, CancellationToken token)
    {
        try
        {
            var otelDemoMessage = args.Message.Body.ToObjectFromJson<OtelDemoMessage>()!;
            using var activity =
                ActivityConfig.Source.CreateActivityFromMessage(args.Message, otelDemoMessage.UseLinks);

            activity?.AddEvent(new ActivityEvent("Processing message"));

            var newMessage = new ServiceBusMessage(args.Message.Body)
            {
                ContentType = "application/json"
            };

            newMessage.InjectContext();
            var destinationQueue = args.Message.ApplicationProperties.TryGetValue(Constants.DestinationQueueName, out var value);

            if (!destinationQueue)
            {
                throw new InvalidOperationException("Destination queue name not found in message");
            }


            _destinationQueues.TryGetValue(value!.ToString()!, out var sender);

            await sender!.SendMessageAsync(newMessage, token);

            await args.CompleteMessageAsync(args.Message, token);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error processing message");
            throw;
        }
    }
}