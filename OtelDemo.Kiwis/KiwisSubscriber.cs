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

        using var activity = ActivityConfig.Source.CreateActivityFromMessage(args.Message, args.Message.Body.ToObjectFromJson<OtelDemoMessage>()!.UseLinks);

        try
        {
            //check if google is up
            var response = await client.GetAsync("https://www.google.com", token);
            response.EnsureSuccessStatusCode();
            activity?.AddEvent(new ActivityEvent("Google is up"));
        }
        catch (Exception e)
        {
            activity?.AddEvent(new ActivityEvent($"Exception: {e.Message}"));
        }
        activity?.AddEvent(new ActivityEvent("Processing complete"));
        await args.CompleteMessageAsync(args.Message, token);
    }
}