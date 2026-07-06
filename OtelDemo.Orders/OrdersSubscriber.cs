using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Options;
using OtelDemo.Shared;

namespace OtelDemo.Orders;

public class OrdersSubscriber(IAzureClientFactory<ServiceBusSender> senderFactory, IAzureClientFactory<ServiceBusProcessor> processorFactory, IOptions<MessageSubscriberSettings> options, ILogger<MessageSubscriberBase> logger) : MessageSubscriberBase(senderFactory, processorFactory, options, logger)
{
    public override async Task HandleMessage(ProcessMessageEventArgs args, CancellationToken token)
    {
        var otelDemoMessage = args.Message.Body.ToObjectFromJson<OtelDemoMessage>()!;
        var stopwatch = Stopwatch.StartNew();
        Activity? activity = null;

        try
        {
            activity = ActivityConfig.Source.CreateActivityFromMessage(args.Message, otelDemoMessage.UseLinks, "orders.process");
            activity.AddMessageTags(otelDemoMessage, args.Message, Constants.OrdersTopic);
            activity?.SetTag("demo.stage", "orders");

            if (args.Message.DeliveryCount > 1)
            {
                DemoObservability.RecordRetryObserved(otelDemoMessage, Constants.OrdersTopic);
            }

            Logger.LogInformation("Orders received message {MessageId} for order {OrderId}, workflow {WorkflowId}, scenario {Scenario}, delivery {DeliveryCount}",
                args.Message.MessageId, otelDemoMessage.OrderId, otelDemoMessage.WorkflowId, otelDemoMessage.Scenario, args.Message.DeliveryCount);

            activity?.AddEvent(new ActivityEvent("order.validated"));
            activity?.AddEvent(new ActivityEvent("order.accepted", tags: new ActivityTagsCollection
            {
                new KeyValuePair<string, object?>("order.id", otelDemoMessage.OrderId),
                new KeyValuePair<string, object?>("order.amount", otelDemoMessage.Amount)
            }));

            await PublishAuditAsync(otelDemoMessage, "order.accepted", args.Message.MessageId, token);

            var destinations = otelDemoMessage.Scenario == DemoScenarios.Fanout
                ? new[] { Constants.InventoryQueue, Constants.PaymentsQueue }
                : [Constants.InventoryQueue];

            foreach (var destination in destinations)
            {
                var newMessage = DemoObservability.CreateServiceBusMessage(otelDemoMessage, args.Message.MessageId);
                newMessage.ApplicationProperties.Add(Constants.DestinationQueueName, destination);
                await GetSender(destination).SendMessageAsync(newMessage, token);

                DemoObservability.RecordForwarded(otelDemoMessage, destination);
                activity?.AddEvent(new ActivityEvent("message.forwarded", tags: new ActivityTagsCollection
                {
                    new KeyValuePair<string, object?>("messaging.destination.name", destination),
                    new KeyValuePair<string, object?>("messaging.message.id", newMessage.MessageId)
                }));
            }

            await args.CompleteMessageAsync(args.Message, token);
            DemoObservability.RecordProcessed(otelDemoMessage, Constants.OrdersTopic, "success");
            DemoObservability.RecordProcessingDuration(otelDemoMessage, Constants.OrdersTopic, stopwatch.Elapsed, "success");
        }
        catch (Exception exception)
        {
            activity.RecordException(exception);
            DemoObservability.RecordFailed(otelDemoMessage, Constants.OrdersTopic);
            DemoObservability.RecordProcessingDuration(otelDemoMessage, Constants.OrdersTopic, stopwatch.Elapsed, "failed");
            Logger.LogError(exception, "Orders failed message {MessageId} for order {OrderId}, workflow {WorkflowId}, scenario {Scenario}, delivery {DeliveryCount}",
                args.Message.MessageId, otelDemoMessage.OrderId, otelDemoMessage.WorkflowId, otelDemoMessage.Scenario, args.Message.DeliveryCount);
            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }

    private async Task PublishAuditAsync(OtelDemoMessage message, string eventType, string parentMessageId, CancellationToken token)
    {
        var auditMessage = DemoObservability.CreateAuditMessage(message, eventType, parentMessageId);
        await GetSender(Constants.AuditTopic).SendMessageAsync(auditMessage, token);
        DemoObservability.RecordForwarded(message, Constants.AuditTopic);
    }
}
