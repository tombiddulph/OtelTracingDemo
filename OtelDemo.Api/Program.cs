using System.Diagnostics;
using System.Text.Json.Serialization;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Azure;
using OpenTelemetry.Trace;
using OtelDemo.Api;
using OtelDemo.Shared;
using Scalar.AspNetCore;
using ActivityConfig = OtelDemo.Api.ActivityConfig;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.AddServiceBus(processorName: Constants.ApiInboxQueue, senderNames: [Constants.OrdersTopic, Constants.NotificationsQueue, Constants.PaymentsQueue, Constants.InventoryQueue]);
builder.AddOpenTelemetry(ActivityConfig.Source.Name, opts => opts.AddAspNetCoreInstrumentation());

builder.Services.AddOptions<HealthCheckSettings>().Bind(builder.Configuration.GetSection("HealthCheckSettings"));
builder.Services.AddOptions<DestinationQueueSettings>().Bind(builder.Configuration.GetSection("DestinationQueues"));
builder.Services.AddOptions<MessageSubscriberSettings>()
    .Bind(builder.Configuration.GetSection("MessageSubscriberSettings"));
builder.Services.AddHealthChecks().AddCheck<ServiceBusHealthCheck>("ServiceBusHealthCheck");
builder.Services.AddHostedService<Resender>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseStatusCodePages();
app.MapHealthChecks("/health");
app.MapOpenApi();
app.MapScalarApiReference();
app.UseHttpsRedirection();

app.MapPost("/start-process", async (
        [FromBody] StartProcessMessage processMessage,
        [FromServices] IAzureClientFactory<ServiceBusSender> senderFactory,
        [FromServices] ILoggerFactory loggerFactory) =>
    {
        var logger = loggerFactory.CreateLogger("StartProcess");
        return await StartProcessAsync(processMessage, senderFactory, logger);
    })
    .WithName("StartProcess")
    .WithDescription("Starts a process with fully configurable OpenTelemetry demo options")
    .WithHttpLogging(HttpLoggingFields.All)
    .Produces<StartProcessResponse>(StatusCodes.Status200OK, "application/json");

app.MapPost("/scenarios/linear-trace", async (IAzureClientFactory<ServiceBusSender> senderFactory, ILoggerFactory loggerFactory) =>
    await StartProcessAsync(new StartProcessMessage(false, 1, false, false, FailureMode.None, DemoScenarios.LinearTrace), senderFactory, loggerFactory.CreateLogger("Scenario.LinearTrace")));

app.MapPost("/scenarios/linked-trace", async (IAzureClientFactory<ServiceBusSender> senderFactory, ILoggerFactory loggerFactory) =>
    await StartProcessAsync(new StartProcessMessage(true, 1, true, false, FailureMode.None, DemoScenarios.LinkedTrace), senderFactory, loggerFactory.CreateLogger("Scenario.LinkedTrace")));

app.MapPost("/scenarios/batch", async (IAzureClientFactory<ServiceBusSender> senderFactory, ILoggerFactory loggerFactory) =>
    await StartProcessAsync(new StartProcessMessage(false, 10, false, true, FailureMode.None, DemoScenarios.Batch), senderFactory, loggerFactory.CreateLogger("Scenario.Batch")));

app.MapPost("/scenarios/fanout", async (IAzureClientFactory<ServiceBusSender> senderFactory, ILoggerFactory loggerFactory) =>
    await StartProcessAsync(new StartProcessMessage(true, 1, true, false, FailureMode.None, DemoScenarios.Fanout), senderFactory, loggerFactory.CreateLogger("Scenario.Fanout")));

app.MapPost("/scenarios/retry-then-success", async (IAzureClientFactory<ServiceBusSender> senderFactory, ILoggerFactory loggerFactory) =>
    await StartProcessAsync(new StartProcessMessage(false, 1, false, false, FailureMode.ThrowOnce, DemoScenarios.RetryThenSuccess), senderFactory, loggerFactory.CreateLogger("Scenario.RetryThenSuccess")));

app.MapPost("/scenarios/dead-letter", async (IAzureClientFactory<ServiceBusSender> senderFactory, ILoggerFactory loggerFactory) =>
    await StartProcessAsync(new StartProcessMessage(false, 1, false, false, FailureMode.AlwaysThrow, DemoScenarios.DeadLetter), senderFactory, loggerFactory.CreateLogger("Scenario.DeadLetter")));

app.MapPost("/scenarios/external-call-failure", async (IAzureClientFactory<ServiceBusSender> senderFactory, ILoggerFactory loggerFactory) =>
    await StartProcessAsync(new StartProcessMessage(false, 1, false, false, FailureMode.ExternalCallFailure, DemoScenarios.ExternalCallFailure), senderFactory, loggerFactory.CreateLogger("Scenario.ExternalCallFailure")));

app.Run();

static async Task<IResult> StartProcessAsync(StartProcessMessage processMessage, IAzureClientFactory<ServiceBusSender> senderFactory, ILogger logger)
{
    using var activity = ActivityConfig.Source.StartActivity("api.start_process");
    var sender = senderFactory.CreateClient(Constants.OrdersTopic);
    var workflowId = Guid.CreateVersion7().ToString();
    var orderId = Guid.CreateVersion7().ToString();
    var scenario = string.IsNullOrWhiteSpace(processMessage.Scenario) ? DemoScenarios.LinearTrace : processMessage.Scenario;
    var messages = new List<ServiceBusMessage>();

    activity?.SetTag("demo.workflow_id", workflowId);
    activity?.SetTag("order.id", orderId);
    activity?.SetTag("demo.scenario", scenario);
    activity?.SetTag("demo.use_links", processMessage.UseLinks);
    activity?.SetTag("demo.start_new_trace", processMessage.StartNewTrace);
    activity?.SetTag("demo.batch", processMessage.Batch);
    activity?.SetTag("demo.failure_mode", processMessage.FailureMode.ToString());

    logger.LogInformation("Starting OpenTelemetry scenario {Scenario} with workflow {WorkflowId}, useLinks={UseLinks}, batch={Batch}, messages={MessageCount}, failureMode={FailureMode}",
        scenario, workflowId, processMessage.UseLinks, processMessage.Batch, processMessage.NumberOfMessages, processMessage.FailureMode);

    for (var i = 0; i < processMessage.NumberOfMessages; i++)
    {
        var demoMessage = new OtelDemoMessage
        {
            WorkflowId = workflowId,
            OrderId = orderId,
            Scenario = scenario,
            Amount = 99.95m + i,
            UseLinks = processMessage.UseLinks,
            StartNewTrace = processMessage.StartNewTrace,
            Batch = processMessage.Batch,
            FailureMode = processMessage.FailureMode,
            Step = 0
        };

        ServiceBusMessage serviceBusMessage;

        if (processMessage.StartNewTrace)
        {
            var currentActivity = Activity.Current;
            Activity.Current = null;

            using (var newActivity = ActivityConfig.Source.StartActivity("api.new_trace"))
            {
                if (processMessage.UseLinks && currentActivity is not null)
                {
                    newActivity?.AddLink(new ActivityLink(currentActivity.Context));
                }

                newActivity.AddMessageTags(demoMessage, destination: Constants.OrdersTopic);
                serviceBusMessage = DemoObservability.CreateServiceBusMessage(demoMessage);
            }

            Activity.Current = currentActivity;
        }
        else
        {
            serviceBusMessage = DemoObservability.CreateServiceBusMessage(demoMessage);
        }

        activity?.AddEvent(new ActivityEvent("message.published", tags: new ActivityTagsCollection
        {
            new KeyValuePair<string, object?>("messaging.message.id", serviceBusMessage.MessageId),
            new KeyValuePair<string, object?>("messaging.destination.name", Constants.OrdersTopic),
            new KeyValuePair<string, object?>("demo.message_index", i)
        }));
        messages.Add(serviceBusMessage);
    }

    if (processMessage.Batch)
    {
        var batch = await sender.CreateMessageBatchAsync();

        foreach (var serviceBusMessage in messages)
        {
            if (!batch.TryAddMessage(serviceBusMessage))
            {
                return Results.BadRequest("Message too large to fit in batch");
            }
        }

        await sender.SendMessagesAsync(batch);
    }
    else
    {
        await sender.SendMessagesAsync(messages);
    }

    DemoObservability.RecordPublished(new OtelDemoMessage { WorkflowId = workflowId, OrderId = orderId, Scenario = scenario, FailureMode = processMessage.FailureMode }, Constants.OrdersTopic, messages.Count);

    return Results.Ok(new StartProcessResponse(
        workflowId,
        scenario,
        processMessage.UseLinks,
        messages.Select(m => m.MessageId).ToList(),
        processMessage.StartNewTrace,
        processMessage.Batch,
        processMessage.FailureMode,
        "Aspire Dashboard",
        $"Search Aspire for workflow id {workflowId} or message id {messages[0].MessageId}"));
}

record StartProcessMessage(bool UseLinks, int NumberOfMessages, bool StartNewTrace, bool Batch, FailureMode FailureMode = FailureMode.None, string? Scenario = null);

record StartProcessResponse(
    string WorkflowId,
    string Scenario,
    bool UseLinks,
    List<string> MessageIds,
    bool StartNewTrace,
    bool Batch,
    FailureMode FailureMode,
    string AspireUrl,
    string SearchHint);

public class HealthCheckSettings
{
    public string SenderName { get; set; } = Constants.NotificationsQueue;
}

public class DestinationQueueSettings
{
    public Dictionary<string, string> Queues { get; set; } = new();
}
