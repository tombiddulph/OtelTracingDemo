using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Options;
using OtelDemo.Shared;

namespace OtelDemo.Payments;

public class PaymentsSubscriber(
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
            activity = ActivityConfig.Source.CreateActivityFromMessage(args.Message, otelDemoMessage.UseLinks, "payments.process");
            activity.AddMessageTags(otelDemoMessage, args.Message, Constants.PaymentsQueue);
            activity?.SetTag("demo.stage", "payments");

            if (args.Message.DeliveryCount > 1)
            {
                DemoObservability.RecordRetryObserved(otelDemoMessage, Constants.PaymentsQueue);
            }

            Logger.LogInformation("Payments received message {MessageId} for order {OrderId}, workflow {WorkflowId}, scenario {Scenario}, delivery {DeliveryCount}",
                args.Message.MessageId, otelDemoMessage.OrderId, otelDemoMessage.WorkflowId, otelDemoMessage.Scenario, args.Message.DeliveryCount);

            await DemoObservability.ApplyFailureModeAsync(otelDemoMessage, args.Message, "Payments", token);

            activity?.AddEvent(new ActivityEvent("payment.authorize", tags: new ActivityTagsCollection
            {
                new KeyValuePair<string, object?>("payment.amount", otelDemoMessage.Amount)
            }));
            await Task.Delay(TimeSpan.FromMilliseconds(100), token);
            activity?.AddEvent(new ActivityEvent("payment.capture"));

            var authorizedMessage = otelDemoMessage with { PaymentAuthorized = true };
            await PublishAuditAsync(authorizedMessage, "payment.authorized", args.Message.MessageId, token);

            var newMessage = DemoObservability.CreateServiceBusMessage(authorizedMessage, args.Message.MessageId);
            newMessage.ApplicationProperties.Add(Constants.DestinationQueueName, Constants.NotificationsQueue);

            await GetSender(Constants.NotificationsQueue).SendMessageAsync(newMessage, token);

            DemoObservability.RecordForwarded(authorizedMessage, Constants.NotificationsQueue);
            await args.CompleteMessageAsync(args.Message, token);
            DemoObservability.RecordProcessed(authorizedMessage, Constants.PaymentsQueue, "success");
            DemoObservability.RecordProcessingDuration(authorizedMessage, Constants.PaymentsQueue, stopwatch.Elapsed, "success");
        }
        catch (Exception exception)
        {
            activity.RecordException(exception);
            DemoObservability.RecordFailed(otelDemoMessage, Constants.PaymentsQueue);
            DemoObservability.RecordProcessingDuration(otelDemoMessage, Constants.PaymentsQueue, stopwatch.Elapsed, "failed");
            Logger.LogError(exception, "Payments failed message {MessageId} for order {OrderId}, workflow {WorkflowId}, scenario {Scenario}, delivery {DeliveryCount}",
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
