using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Options;
using OtelDemo.Shared;

namespace OtelDemo.Pears;

public class PearsSubscriber(
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
            activity = ActivityConfig.Source.CreateActivityFromMessage(args.Message, otelDemoMessage.UseLinks, "pears.process");
            activity.AddMessageTags(otelDemoMessage, args.Message, "pears");

            if (args.Message.DeliveryCount > 1)
            {
                DemoObservability.RecordRetryObserved(otelDemoMessage, "pears");
            }

            Logger.LogInformation("Pears received message {MessageId} for workflow {WorkflowId}, scenario {Scenario}, delivery {DeliveryCount}",
                args.Message.MessageId, otelDemoMessage.WorkflowId, otelDemoMessage.Scenario, args.Message.DeliveryCount);

            await DemoObservability.ApplyFailureModeAsync(otelDemoMessage, args.Message, "Pears", token);

            activity?.AddEvent(new ActivityEvent("message.received"));
            var newMessage = DemoObservability.CreateServiceBusMessage(otelDemoMessage, args.Message.MessageId);
            newMessage.ApplicationProperties.Add(Constants.DestinationQueueName, "kiwis");

            await Sender!.SendMessageAsync(newMessage, token);

            DemoObservability.RecordForwarded(otelDemoMessage, "kiwis");
            await args.CompleteMessageAsync(args.Message, token);
            DemoObservability.RecordProcessed(otelDemoMessage, "pears", "success");
            DemoObservability.RecordProcessingDuration(otelDemoMessage, "pears", stopwatch.Elapsed, "success");
        }
        catch (Exception exception)
        {
            activity.RecordException(exception);
            DemoObservability.RecordFailed(otelDemoMessage, "pears");
            DemoObservability.RecordProcessingDuration(otelDemoMessage, "pears", stopwatch.Elapsed, "failed");
            Logger.LogError(exception, "Pears failed message {MessageId} for workflow {WorkflowId}, scenario {Scenario}, delivery {DeliveryCount}",
                args.Message.MessageId, otelDemoMessage.WorkflowId, otelDemoMessage.Scenario, args.Message.DeliveryCount);
            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }
}
