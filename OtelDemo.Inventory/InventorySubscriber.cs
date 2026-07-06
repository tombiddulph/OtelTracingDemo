using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Options;
using OtelDemo.Shared;

namespace OtelDemo.Inventory;

public class InventorySubscriber(
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
            activity = ActivityConfig.Source.CreateActivityFromMessage(args.Message, otelDemoMessage.UseLinks, "inventory.process");
            activity.AddMessageTags(otelDemoMessage, args.Message, Constants.InventoryQueue);
            activity?.SetTag("demo.stage", "inventory");

            if (args.Message.DeliveryCount > 1)
            {
                DemoObservability.RecordRetryObserved(otelDemoMessage, Constants.InventoryQueue);
            }

            Logger.LogInformation("Inventory received message {MessageId} for order {OrderId}, workflow {WorkflowId}, scenario {Scenario}, delivery {DeliveryCount}",
                args.Message.MessageId, otelDemoMessage.OrderId, otelDemoMessage.WorkflowId, otelDemoMessage.Scenario, args.Message.DeliveryCount);

            if (otelDemoMessage.FailureMode == FailureMode.SlowProcessing)
            {
                await DemoObservability.ApplyFailureModeAsync(otelDemoMessage, args.Message, "Inventory", token);
            }

            activity?.AddEvent(new ActivityEvent("inventory.lookup"));
            await Task.Delay(TimeSpan.FromMilliseconds(75), token);
            activity?.AddEvent(new ActivityEvent("inventory.reserve"));

            var reservedMessage = otelDemoMessage with { InventoryReserved = true };
            await PublishAuditAsync(reservedMessage, "inventory.reserved", args.Message.MessageId, token);

            var newMessage = DemoObservability.CreateServiceBusMessage(reservedMessage, args.Message.MessageId);
            newMessage.ApplicationProperties.Add(Constants.DestinationQueueName, Constants.PaymentsQueue);

            await GetSender(Constants.PaymentsQueue).SendMessageAsync(newMessage, token);

            DemoObservability.RecordForwarded(reservedMessage, Constants.PaymentsQueue);
            await args.CompleteMessageAsync(args.Message, token);
            DemoObservability.RecordProcessed(reservedMessage, Constants.InventoryQueue, "success");
            DemoObservability.RecordProcessingDuration(reservedMessage, Constants.InventoryQueue, stopwatch.Elapsed, "success");
        }
        catch (Exception exception)
        {
            activity.RecordException(exception);
            DemoObservability.RecordFailed(otelDemoMessage, Constants.InventoryQueue);
            DemoObservability.RecordProcessingDuration(otelDemoMessage, Constants.InventoryQueue, stopwatch.Elapsed, "failed");
            Logger.LogError(exception, "Inventory failed message {MessageId} for order {OrderId}, workflow {WorkflowId}, scenario {Scenario}, delivery {DeliveryCount}",
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
