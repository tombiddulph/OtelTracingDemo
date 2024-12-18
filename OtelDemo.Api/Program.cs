using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Azure;
using OpenTelemetry;
using OpenTelemetry.Trace;
using OtelDemo.Api;
using OtelDemo.Shared;
using ActivityConfig = OtelDemo.Api.ActivityConfig;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.AddServiceBus(processorName:"mangoes", senderNames:["api-topic", "kiwis","pears", "peaches" ]);
builder.AddOpenTelemetry(ActivityConfig.Source.Name, opts => opts.AddAspNetCoreInstrumentation());

builder.Services.AddOptions<HealthCheckSettings>().Bind(builder.Configuration.GetSection("HealthCheckSettings"));

builder.Services.AddOptions<DestinationQueueSettings>().Bind(builder.Configuration.GetSection("DestinationQueues"));
builder.Services.AddOptions<MessageSubscriberSettings>().Bind(builder.Configuration.GetSection("MessageSubscriberSettings"));
builder.Services.AddHealthChecks().AddCheck<ServiceBusHealthCheck>("ServiceBusHealthCheck");
builder.Services.AddHostedService<Resender>();
var app = builder.Build();

app.MapHealthChecks("/health");
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapPost("/start-process", async ([FromBody] StartProcessMessage processMessage, [FromServices]IAzureClientFactory<ServiceBusSender> senderFactory) =>
        {
            using var activity = ActivityConfig.Source.StartActivity("StartProcess");
            var sender = senderFactory.CreateClient("api-topic");
            var messages = new List<ServiceBusMessage>();
            for (var i = 0; i < processMessage.NumberOfMessages; i++)
            {
                var message = new ServiceBusMessage(JsonSerializer.Serialize(processMessage))
                {
                    ContentType = "application/json",
                    MessageId = Guid.CreateVersion7(DateTimeOffset.Now).ToString()
                };
                activity?.AddEvent(new ActivityEvent("Sending message",
                    tags: new ActivityTagsCollection
                        { new KeyValuePair<string, object?>("MessageId", message.MessageId) }));
                
                Baggage.SetBaggage("UseLinks", processMessage.UseLinks.ToString());

                if (processMessage.StartNewTrace)
                {
                    var currentActivity = Activity.Current;
                    Activity.Current = null;
                    var newActivity = ActivityConfig.Source.StartActivity("NewTrace");
                    message.InjectContext();
                    messages.Add(message);
                    newActivity?.Dispose();
                    Activity.Current = currentActivity;
                }
                else
                {
                    message.InjectContext();
                    messages.Add(message);
                }
                
                
            }

            await sender.SendMessagesAsync(messages);
            return Results.Ok(new StartProcessResponse(processMessage.UseLinks, messages.Select(m => m.MessageId).ToList(), processMessage.StartNewTrace));
        })
    .WithName("StartProcess")
    .WithDescription("Starts a process that sends messages to the api-topic queue")
    .WithHttpLogging(HttpLoggingFields.All)
    .Produces<StartProcessMessage>(200);

app.Run();


record StartProcessMessage(bool UseLinks, int NumberOfMessages, bool StartNewTrace);

record StartProcessResponse(bool UseLinks, List<string> messageIds, bool StartNewTrace);

public class HealthCheckSettings
{
    public string SenderName { get; set; } = "kiwis";
}

public class DestinationQueueSettings
{
    public Dictionary<string, string> Queues { get; set; } = new();
}

