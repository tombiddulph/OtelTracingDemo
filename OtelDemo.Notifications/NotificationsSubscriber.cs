using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Options;
using OtelDemo.Shared;

namespace OtelDemo.Notifications;

public class NotificationsSubscriber(
    IAzureClientFactory<ServiceBusSender> senderFactory,
    IAzureClientFactory<ServiceBusProcessor> processorFactory,
    IOptions<MessageSubscriberSettings> options,
    ILogger<MessageSubscriberBase> logger,
    HttpClient client) : MessageSubscriberBase(senderFactory, processorFactory, options, logger)
{
    public override async Task HandleMessage(ProcessMessageEventArgs args, CancellationToken token)
    {
        if (args.Message.ApplicationProperties.TryGetValue(Constants.MessageType, out var value) && value?.ToString() is Constants.HealthCheck)
        {
            await args.CompleteMessageAsync(args.Message, token);
            Logger.LogInformation("Health check message received");
            return;
        }

        var otelDemoMessage = args.Message.Body.ToObjectFromJson<OtelDemoMessage>()!;
        var stopwatch = Stopwatch.StartNew();
        Activity? activity = null;

        try
        {
            activity = ActivityConfig.Source.CreateActivityFromMessage(args.Message, otelDemoMessage.UseLinks, "notifications.process");
            activity.AddMessageTags(otelDemoMessage, args.Message, Constants.NotificationsQueue);
            activity?.SetTag("demo.stage", "notifications");

            if (args.Message.DeliveryCount > 1)
            {
                DemoObservability.RecordRetryObserved(otelDemoMessage, Constants.NotificationsQueue);
            }

            Logger.LogInformation("Notifications received message {MessageId} for order {OrderId}, workflow {WorkflowId}, scenario {Scenario}, delivery {DeliveryCount}",
                args.Message.MessageId, otelDemoMessage.OrderId, otelDemoMessage.WorkflowId, otelDemoMessage.Scenario, args.Message.DeliveryCount);

            activity?.AddEvent(new ActivityEvent("notification.email"));
            var url = otelDemoMessage.FailureMode == FailureMode.ExternalCallFailure
                ? "https://localhost:1/otel-demo-intentional-failure"
                : "https://www.google.com";

            activity?.AddEvent(new ActivityEvent("notification.webhook.started", tags: new ActivityTagsCollection
            {
                new KeyValuePair<string, object?>("url.full", url)
            }));

            var response = await client.GetAsync(url, token);
            response.EnsureSuccessStatusCode();

            activity?.AddEvent(new ActivityEvent("notification.webhook.completed", tags: new ActivityTagsCollection
            {
                new KeyValuePair<string, object?>("http.response.status_code", (int)response.StatusCode)
            }));

            var sentMessage = otelDemoMessage with { NotificationSent = true };
            await PublishAuditAsync(sentMessage, "notification.sent", args.Message.MessageId, token);

            await args.CompleteMessageAsync(args.Message, token);
            DemoObservability.RecordProcessed(sentMessage, Constants.NotificationsQueue, "success");
            DemoObservability.RecordProcessingDuration(sentMessage, Constants.NotificationsQueue, stopwatch.Elapsed, "success");
        }
        catch (Exception e)
        {
            activity.RecordException(e);
            DemoObservability.RecordFailed(otelDemoMessage, Constants.NotificationsQueue);
            DemoObservability.RecordProcessingDuration(otelDemoMessage, Constants.NotificationsQueue, stopwatch.Elapsed, "failed");
            Logger.LogError(e, "Notifications failed message {MessageId} for order {OrderId}, workflow {WorkflowId}, scenario {Scenario}, delivery {DeliveryCount}",
                args.Message.MessageId, otelDemoMessage.OrderId, otelDemoMessage.WorkflowId, otelDemoMessage.Scenario, args.Message.DeliveryCount);

            if (otelDemoMessage.FailureMode == FailureMode.ExternalCallFailure)
            {
                await args.CompleteMessageAsync(args.Message, token);
                return;
            }

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
