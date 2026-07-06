using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Options;
using OtelDemo.Shared;

namespace OtelDemo.Peaches;

public class PeachesSubscriber(
    IAzureClientFactory<ServiceBusSender> senderFactory,
    IAzureClientFactory<ServiceBusProcessor> processorFactory,
    IOptions<MessageSubscriberSettings> options,
    ILogger<MessageSubscriberBase> logger) : MessageSubscriberBase(senderFactory, processorFactory, options, logger)
{
    public override async Task HandleMessage(ProcessMessageEventArgs args, CancellationToken token)
    {
        var otelDemoMessage = args.Message.Body.ToObjectFromJson<OtelDemoMessage>()!;
        var stopwatch = Stopwatch.StartNew();
        Activity? activity = null;

        try
        {
            activity = ActivityConfig.Source.CreateActivityFromMessage(args.Message, otelDemoMessage.UseLinks, "peaches.process");
            activity.AddMessageTags(otelDemoMessage, args.Message, "peaches");

            if (args.Message.DeliveryCount > 1)
            {
                DemoObservability.RecordRetryObserved(otelDemoMessage, "peaches");
            }

            Logger.LogInformation("Peaches received message {MessageId} for workflow {WorkflowId}, scenario {Scenario}, delivery {DeliveryCount}",
                args.Message.MessageId, otelDemoMessage.WorkflowId, otelDemoMessage.Scenario, args.Message.DeliveryCount);

            await DemoObservability.ApplyFailureModeAsync(otelDemoMessage, args.Message, "Peaches", token);

            activity?.AddEvent(new ActivityEvent("message.received"));
            var newMessage = DemoObservability.CreateServiceBusMessage(otelDemoMessage, args.Message.MessageId);
            newMessage.ApplicationProperties.Add(Constants.DestinationQueueName, "pears");
            await Sender!.SendMessageAsync(newMessage, token);

            DemoObservability.RecordForwarded(otelDemoMessage, "pears");
            await args.CompleteMessageAsync(args.Message, token);
            DemoObservability.RecordProcessed(otelDemoMessage, "peaches", "success");
            DemoObservability.RecordProcessingDuration(otelDemoMessage, "peaches", stopwatch.Elapsed, "success");
        }
        catch (Exception exception)
        {
            activity.RecordException(exception);
            DemoObservability.RecordFailed(otelDemoMessage, "peaches");
            DemoObservability.RecordProcessingDuration(otelDemoMessage, "peaches", stopwatch.Elapsed, "failed");
            Logger.LogError(exception, "Peaches failed message {MessageId} for workflow {WorkflowId}, scenario {Scenario}, delivery {DeliveryCount}",
                args.Message.MessageId, otelDemoMessage.WorkflowId, otelDemoMessage.Scenario, args.Message.DeliveryCount);
            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }
}
