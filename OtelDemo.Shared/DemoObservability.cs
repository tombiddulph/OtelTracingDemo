using System.Diagnostics;
using System.Diagnostics.Metrics;
using Azure.Messaging.ServiceBus;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace OtelDemo.Shared;

public static class DemoObservability
{
    public const string MeterName = "OtelDemo";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> MessagesPublished = Meter.CreateCounter<long>("oteldemo.messages.published");
    private static readonly Counter<long> MessagesProcessed = Meter.CreateCounter<long>("oteldemo.messages.processed");
    private static readonly Counter<long> MessagesFailed = Meter.CreateCounter<long>("oteldemo.messages.failed");
    private static readonly Counter<long> MessagesForwarded = Meter.CreateCounter<long>("oteldemo.messages.forwarded");
    private static readonly Counter<long> RetriesObserved = Meter.CreateCounter<long>("oteldemo.retries.observed");
    private static readonly Histogram<double> ProcessingDuration = Meter.CreateHistogram<double>("oteldemo.processing.duration", "ms");

    public static void AddMessageTags(this Activity? activity, OtelDemoMessage message, ServiceBusReceivedMessage? receivedMessage = null, string? destination = null)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag("demo.workflow_id", message.WorkflowId);
        activity.SetTag("demo.scenario", message.Scenario);
        activity.SetTag("demo.use_links", message.UseLinks);
        activity.SetTag("demo.start_new_trace", message.StartNewTrace);
        activity.SetTag("demo.batch", message.Batch);
        activity.SetTag("demo.failure_mode", message.FailureMode.ToString());
        activity.SetTag("demo.step", message.Step);
        activity.SetTag("messaging.system", "azure_service_bus");

        if (!string.IsNullOrEmpty(destination))
        {
            activity.SetTag("messaging.destination.name", destination);
        }

        if (receivedMessage is null)
        {
            return;
        }

        activity.SetTag("messaging.message.id", receivedMessage.MessageId);
        activity.SetTag("messaging.servicebus.delivery_count", receivedMessage.DeliveryCount);
    }

    public static void RecordPublished(OtelDemoMessage message, string destination, long count = 1) =>
        MessagesPublished.Add(count, CreateTags(message, destination, "published"));

    public static void RecordProcessed(OtelDemoMessage message, string queue, string outcome) =>
        MessagesProcessed.Add(1, CreateTags(message, queue, outcome));

    public static void RecordFailed(OtelDemoMessage message, string queue) =>
        MessagesFailed.Add(1, CreateTags(message, queue, "failed"));

    public static void RecordForwarded(OtelDemoMessage message, string destination) =>
        MessagesForwarded.Add(1, CreateTags(message, destination, "forwarded"));

    public static void RecordRetryObserved(OtelDemoMessage message, string queue) =>
        RetriesObserved.Add(1, CreateTags(message, queue, "retry"));

    public static void RecordProcessingDuration(OtelDemoMessage message, string queue, TimeSpan duration, string outcome) =>
        ProcessingDuration.Record(duration.TotalMilliseconds, CreateTags(message, queue, outcome));

    public static void RecordException(this Activity? activity, Exception exception)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.AddException(exception);
    }

    public static void SetBaggage(OtelDemoMessage message)
    {
        Baggage.SetBaggage("workflow.id", message.WorkflowId);
        Baggage.SetBaggage("demo.scenario", message.Scenario);
        Baggage.SetBaggage("demo.trace_mode", message.UseLinks ? "links" : "parent");
    }

    public static ServiceBusMessage CreateServiceBusMessage(OtelDemoMessage message, string? parentMessageId = null)
    {
        var outgoingMessage = message with
        {
            Step = message.Step + 1,
            ParentMessageId = parentMessageId ?? message.ParentMessageId
        };

        SetBaggage(outgoingMessage);

        var serviceBusMessage = new ServiceBusMessage(BinaryData.FromObjectAsJson(outgoingMessage))
        {
            ContentType = "application/json",
            MessageId = Guid.CreateVersion7().ToString(),
            CorrelationId = outgoingMessage.WorkflowId
        };

        serviceBusMessage.ApplicationProperties["WorkflowId"] = outgoingMessage.WorkflowId;
        serviceBusMessage.ApplicationProperties["Scenario"] = outgoingMessage.Scenario;
        serviceBusMessage.ApplicationProperties["FailureMode"] = outgoingMessage.FailureMode.ToString();
        serviceBusMessage.InjectContext();

        return serviceBusMessage;
    }

    public static async Task ApplyFailureModeAsync(OtelDemoMessage message, ServiceBusReceivedMessage receivedMessage, string serviceName, CancellationToken token)
    {
        if (message.FailureMode == FailureMode.SlowProcessing)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), token);
            return;
        }

        if (message.FailureMode == FailureMode.ThrowOnce && receivedMessage.DeliveryCount == 1)
        {
            throw new InvalidOperationException($"{serviceName} simulated first-delivery failure for workflow {message.WorkflowId}");
        }

        if (message.FailureMode == FailureMode.AlwaysThrow)
        {
            throw new InvalidOperationException($"{serviceName} simulated persistent failure for workflow {message.WorkflowId}");
        }
    }

    private static TagList CreateTags(OtelDemoMessage message, string destination, string outcome) =>
    [
        new KeyValuePair<string, object?>("scenario", message.Scenario),
        new KeyValuePair<string, object?>("workflow_id", message.WorkflowId),
        new KeyValuePair<string, object?>("destination", destination),
        new KeyValuePair<string, object?>("outcome", outcome),
        new KeyValuePair<string, object?>("failure_mode", message.FailureMode.ToString())
    ];
}
