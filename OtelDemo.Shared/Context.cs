using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace OtelDemo.Shared;

public static class ContextHelper
{
    public static Activity? CreateActivityFromMessage(this ActivitySource source, ServiceBusReceivedMessage message, bool useLinks)
    {
        Activity? activity = null;
        var context = Propagators.DefaultTextMapPropagator.Extract(default, message.ApplicationProperties,
            (props, key) =>
            {
                if (props.TryGetValue(key, out var val) && val is string str)
                {
                    return [str];
                }

                return [];
            });


        if (useLinks)
        {
            activity = source.StartActivity("ProcessMessage", ActivityKind.Consumer);
            activity?.AddLink(new ActivityLink(context.ActivityContext));
        }
        else
        {
            activity = context.ActivityContext.IsValid()
                ? source.StartActivity("ProcessMessage", ActivityKind.Consumer, context.ActivityContext)
                : source.StartActivity(name: "ProcessMessage", ActivityKind.Consumer);
        }
     

        return activity;
    }

    public static void InjectContext(this ServiceBusMessage message)
    {
        if (Activity.Current is not null)
        {
            Propagators.DefaultTextMapPropagator.Inject(new PropagationContext(Activity.Current.Context, Baggage.Current),
                message.ApplicationProperties,
                (props, key, value) => props[key] = value);
        }
    }
}