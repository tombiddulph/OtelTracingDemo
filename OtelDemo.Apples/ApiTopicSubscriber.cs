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
        var stopwatch = Stopwatch.StartNew();
        Activity? activity = null;

        try
        {
            activity = ActivityConfig.Source.CreateActivityFromMessage(args.Message, otelDemoMessage.UseLinks, "apples.process");
            activity.AddMessageTags(otelDemoMessage, args.Message, "api-topic");

            if (args.Message.DeliveryCount > 1)
            {
                DemoObservability.RecordRetryObserved(otelDemoMessage, "api-topic");
            }

            Logger.LogInformation("Apples received message {MessageId} for workflow {WorkflowId}, scenario {Scenario}, delivery {DeliveryCount}",
                args.Message.MessageId, otelDemoMessage.WorkflowId, otelDemoMessage.Scenario, args.Message.DeliveryCount);

            await DemoObservability.ApplyFailureModeAsync(otelDemoMessage, args.Message, "Apples", token);

            activity?.AddEvent(new ActivityEvent("message.received"));
            var destinations = otelDemoMessage.Scenario == DemoScenarios.Fanout
                ? new[] { "peaches", "pears" }
                : ["peaches"];

            foreach (var destination in destinations)
            {
                var newMessage = DemoObservability.CreateServiceBusMessage(otelDemoMessage, args.Message.MessageId);
                newMessage.ApplicationProperties.Add(Constants.DestinationQueueName, destination);
                await Sender!.SendMessageAsync(newMessage, token);

                DemoObservability.RecordForwarded(otelDemoMessage, destination);
                activity?.AddEvent(new ActivityEvent("message.forwarded", tags: new ActivityTagsCollection
                {
                    new KeyValuePair<string, object?>("messaging.destination.name", destination),
                    new KeyValuePair<string, object?>("messaging.message.id", newMessage.MessageId)
                }));
            }

            await args.CompleteMessageAsync(args.Message, token);
            DemoObservability.RecordProcessed(otelDemoMessage, "api-topic", "success");
            DemoObservability.RecordProcessingDuration(otelDemoMessage, "api-topic", stopwatch.Elapsed, "success");
        }
        catch (Exception exception)
        {
            activity.RecordException(exception);
            DemoObservability.RecordFailed(otelDemoMessage, "api-topic");
            DemoObservability.RecordProcessingDuration(otelDemoMessage, "api-topic", stopwatch.Elapsed, "failed");
            Logger.LogError(exception, "Apples failed message {MessageId} for workflow {WorkflowId}, scenario {Scenario}, delivery {DeliveryCount}",
                args.Message.MessageId, otelDemoMessage.WorkflowId, otelDemoMessage.Scenario, args.Message.DeliveryCount);
            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }
}
