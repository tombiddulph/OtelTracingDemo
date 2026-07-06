using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Options;
using OtelDemo.Shared;

namespace OtelDemo.Audit;

public class AuditSubscriber(
    IAzureClientFactory<ServiceBusProcessor> processorFactory,
    IOptions<MessageSubscriberSettings> options,
    ILogger<MessageSubscriberBase> logger) : MessageSubscriberBase(processorFactory, options, logger)
{
    public override async Task HandleMessage(ProcessMessageEventArgs args, CancellationToken token)
    {
        var otelDemoMessage = args.Message.Body.ToObjectFromJson<OtelDemoMessage>()!;
        var eventType = args.Message.ApplicationProperties.TryGetValue(Constants.AuditEventType, out var value)
            ? value?.ToString() ?? "audit.record"
            : "audit.record";
        var stopwatch = Stopwatch.StartNew();
        Activity? activity = null;

        try
        {
            activity = ActivityConfig.Source.CreateActivityFromMessage(args.Message, useLinks: true, "audit.record");
            activity.AddMessageTags(otelDemoMessage, args.Message, Constants.AuditTopic);
            activity?.SetTag("audit.event_type", eventType);
            activity?.SetTag("demo.stage", "audit");
            activity?.AddEvent(new ActivityEvent("audit.persist", tags: new ActivityTagsCollection
            {
                new KeyValuePair<string, object?>("audit.event_type", eventType),
                new KeyValuePair<string, object?>("order.id", otelDemoMessage.OrderId)
            }));

            Logger.LogInformation("Audit recorded {EventType} for order {OrderId}, workflow {WorkflowId}", eventType, otelDemoMessage.OrderId, otelDemoMessage.WorkflowId);

            await args.CompleteMessageAsync(args.Message, token);
            DemoObservability.RecordProcessed(otelDemoMessage, Constants.AuditTopic, "success");
            DemoObservability.RecordProcessingDuration(otelDemoMessage, Constants.AuditTopic, stopwatch.Elapsed, "success");
        }
        catch (Exception exception)
        {
            activity.RecordException(exception);
            DemoObservability.RecordFailed(otelDemoMessage, Constants.AuditTopic);
            DemoObservability.RecordProcessingDuration(otelDemoMessage, Constants.AuditTopic, stopwatch.Elapsed, "failed");
            Logger.LogError(exception, "Audit failed message {MessageId} for order {OrderId}, workflow {WorkflowId}", args.Message.MessageId, otelDemoMessage.OrderId, otelDemoMessage.WorkflowId);
            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }
}
