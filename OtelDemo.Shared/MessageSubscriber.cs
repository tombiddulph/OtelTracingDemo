using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OtelDemo.Shared;

public abstract class MessageSubscriberBase : BackgroundService, IAsyncDisposable
{
    private CancellationTokenSource _cts = null!;
    private readonly ServiceBusProcessor _processor;
    protected readonly ServiceBusSender? Sender;
    protected readonly ILogger<MessageSubscriberBase> Logger;

    protected MessageSubscriberBase(IAzureClientFactory<ServiceBusSender> senderFactory,
        IAzureClientFactory<ServiceBusProcessor> processorFactory,
        IOptions<MessageSubscriberSettings> options,
        ILogger<MessageSubscriberBase> logger)
    {
        Logger = logger;
        _processor = processorFactory.CreateClient(options.Value.ProcessorQueueOrTopicName);


        if (options.Value.SenderName != null)
        {
            Sender = senderFactory?.CreateClient(options.Value.SenderName)!;
        }

    }

    protected MessageSubscriberBase(
        IAzureClientFactory<ServiceBusProcessor> processorFactory,
        IOptions<MessageSubscriberSettings> options,
        ILogger<MessageSubscriberBase> logger)
    {
        Logger = logger;
        _processor = processorFactory.CreateClient(options.Value.ProcessorQueueOrTopicName);
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;
        await _processor.StartProcessingAsync(CancellationToken.None);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        await CompleteOnCancelAsync(stoppingToken);
        Logger.LogInformation("Message subscriber is stopping");
        await _cts.CancelAsync();
        await _processor.StopProcessingAsync(CancellationToken.None);
        Logger.LogInformation("Message subscriber has stopped");
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs arg)
    {
        Logger.LogError(arg.Exception, "Error processing message");
        return Task.CompletedTask;
    }

    private Task ProcessMessageAsync(ProcessMessageEventArgs arg) => HandleMessage(arg, _cts.Token);

    protected virtual ValueTask DisposeAsyncCore()
    {
        // TODO release managed resources here

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }



    public abstract Task HandleMessage(ProcessMessageEventArgs args, CancellationToken token);

    private static Task CompleteOnCancelAsync(CancellationToken token)
    {
        var tcs = new TaskCompletionSource();
        token.Register(t =>
        {
            if (t is TaskCompletionSource taskCompletionSource)
            {
                taskCompletionSource.TrySetResult();
            }
        }, tcs);

        return tcs.Task;
    }
}

public class MessageSubscriberSettings
{
    public string ProcessorQueueOrTopicName { get; set; } = null!;
    public string? SenderName { get; set; } = null!;

    public string SubscriptionName { get; set; } = null!;

    public List<string> Senders = [];
}