using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Options;
using OtelDemo.Shared;

namespace OtelDemo.Kiwis;

public class KiwisSubscriber(
    IAzureClientFactory<ServiceBusProcessor> processorFactory,
    IOptions<MessageSubscriberSettings> options,
    ILogger<MessageSubscriberBase> logger,
    HttpClient client) : MessageSubscriberBase(processorFactory, options, logger)
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
            activity = ActivityConfig.Source.CreateActivityFromMessage(args.Message, otelDemoMessage.UseLinks, "kiwis.process");
            activity.AddMessageTags(otelDemoMessage, args.Message, "kiwis");

            if (args.Message.DeliveryCount > 1)
            {
                DemoObservability.RecordRetryObserved(otelDemoMessage, "kiwis");
            }

            Logger.LogInformation("Kiwis received message {MessageId} for workflow {WorkflowId}, scenario {Scenario}, delivery {DeliveryCount}",
                args.Message.MessageId, otelDemoMessage.WorkflowId, otelDemoMessage.Scenario, args.Message.DeliveryCount);

            await DemoObservability.ApplyFailureModeAsync(otelDemoMessage, args.Message, "Kiwis", token);

            var url = otelDemoMessage.FailureMode == FailureMode.ExternalCallFailure
                ? "https://localhost:1/otel-demo-intentional-failure"
                : "https://www.google.com";

            activity?.AddEvent(new ActivityEvent("external_call.started", tags: new ActivityTagsCollection
            {
                new KeyValuePair<string, object?>("url.full", url)
            }));

            var response = await client.GetAsync(url, token);
            response.EnsureSuccessStatusCode();

            activity?.AddEvent(new ActivityEvent("external_call.completed", tags: new ActivityTagsCollection
            {
                new KeyValuePair<string, object?>("http.response.status_code", (int)response.StatusCode)
            }));

            await args.CompleteMessageAsync(args.Message, token);
            DemoObservability.RecordProcessed(otelDemoMessage, "kiwis", "success");
            DemoObservability.RecordProcessingDuration(otelDemoMessage, "kiwis", stopwatch.Elapsed, "success");
        }
        catch (Exception e)
        {
            activity.RecordException(e);
            DemoObservability.RecordFailed(otelDemoMessage, "kiwis");
            DemoObservability.RecordProcessingDuration(otelDemoMessage, "kiwis", stopwatch.Elapsed, "failed");
            Logger.LogError(e, "Kiwis failed message {MessageId} for workflow {WorkflowId}, scenario {Scenario}, delivery {DeliveryCount}",
                args.Message.MessageId, otelDemoMessage.WorkflowId, otelDemoMessage.Scenario, args.Message.DeliveryCount);

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
}
