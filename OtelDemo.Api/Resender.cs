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
        var stopwatch = Stopwatch.StartNew();
        Activity? activity = null;
        OtelDemoMessage? otelDemoMessage = null;

        try
        {
            otelDemoMessage = args.Message.Body.ToObjectFromJson<OtelDemoMessage>()!;
            activity = ActivityConfig.Source.CreateActivityFromMessage(args.Message, otelDemoMessage.UseLinks, "api.route_message");

            if (args.Message.DeliveryCount > 1)
            {
                DemoObservability.RecordRetryObserved(otelDemoMessage, Constants.ApiInboxQueue);
            }

            activity.AddMessageTags(otelDemoMessage, args.Message, Constants.ApiInboxQueue);
            Logger.LogInformation("API resender received message {MessageId} for order {OrderId}, workflow {WorkflowId}, scenario {Scenario}, delivery {DeliveryCount}",
                args.Message.MessageId, otelDemoMessage.OrderId, otelDemoMessage.WorkflowId, otelDemoMessage.Scenario, args.Message.DeliveryCount);

            await DemoObservability.ApplyFailureModeAsync(otelDemoMessage, args.Message, "API resender", token);

            var newMessage = DemoObservability.CreateServiceBusMessage(otelDemoMessage, args.Message.MessageId);

            var destinationQueue = args.Message.ApplicationProperties.TryGetValue(Constants.DestinationQueueName, out var value);

            if (!destinationQueue)
            {
                throw new InvalidOperationException("Destination queue name not found in message");
            }


            if (!_destinationQueues.TryGetValue(value!.ToString()!, out var sender))
            {
                throw new InvalidOperationException($"Destination queue '{value}' is not configured");
            }

            await sender.SendMessageAsync(newMessage, token);

            DemoObservability.RecordForwarded(otelDemoMessage, value!.ToString()!);
            activity?.AddEvent(new ActivityEvent("message.routed", tags: new ActivityTagsCollection
            {
                new KeyValuePair<string, object?>("messaging.destination.name", value!.ToString()),
                new KeyValuePair<string, object?>("messaging.message.id", newMessage.MessageId)
            }));

            await args.CompleteMessageAsync(args.Message, token);
            DemoObservability.RecordProcessed(otelDemoMessage, Constants.ApiInboxQueue, "success");
            DemoObservability.RecordProcessingDuration(otelDemoMessage, Constants.ApiInboxQueue, stopwatch.Elapsed, "success");
        }
        catch (Exception e)
        {
            activity.RecordException(e);

            if (otelDemoMessage is not null)
            {
                DemoObservability.RecordFailed(otelDemoMessage, Constants.ApiInboxQueue);
                DemoObservability.RecordProcessingDuration(otelDemoMessage, Constants.ApiInboxQueue, stopwatch.Elapsed, "failed");
            }

            Logger.LogError(e, "API resender failed message {MessageId} for workflow {WorkflowId}, delivery {DeliveryCount}",
                args.Message.MessageId, otelDemoMessage?.WorkflowId, args.Message.DeliveryCount);
            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }
}
