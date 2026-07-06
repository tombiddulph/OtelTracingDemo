using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace OtelDemo.Shared;

public static class ContextHelper
{
    public static Activity? CreateActivityFromMessage(this ActivitySource source, ServiceBusReceivedMessage message, bool useLinks, string activityName = "ProcessMessage")
    {
        Activity? activity;
        var context = Propagators.DefaultTextMapPropagator.Extract(default, message.ApplicationProperties,
            (props, key) =>
            {
                if (props.TryGetValue(key, out var val) && val is string str)
                {
                    return [str];
                }

                return [];
            });

        Baggage.Current = context.Baggage;

        if (useLinks)
        {
            activity = source.StartActivity(activityName, ActivityKind.Consumer);
            activity?.AddLink(new ActivityLink(context.ActivityContext));
        }
        else
        {
            var propagationContext = context.ActivityContext.IsValid() switch
            {
                true => context,
                false => default
            };

            activity = source.StartActivity(activityName, ActivityKind.Consumer, propagationContext.ActivityContext);


        }


        activity?.SetTag("baggage.workflow.id", Baggage.GetBaggage("workflow.id"));
        activity?.SetTag("baggage.demo.scenario", Baggage.GetBaggage("demo.scenario"));
        activity?.SetTag("baggage.demo.trace_mode", Baggage.GetBaggage("demo.trace_mode"));

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
