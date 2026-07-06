using OtelDemo.Orders;
using OtelDemo.Shared;

var builder = Host.CreateApplicationBuilder(args);
builder.AddOpenTelemetry("OtelDemo.Orders");
builder.AddServiceBus(Constants.OrdersTopic, Constants.OrdersSubscription, Constants.InventoryQueue, Constants.PaymentsQueue, Constants.AuditTopic);
builder.Services.AddOptions<MessageSubscriberSettings>().Bind(builder.Configuration.GetSection("MessageSubscriberSettings"));
builder.Services.AddHostedService<OrdersSubscriber>();
builder.Services.Configure<HostOptions>(o =>
{
    o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});
var host = builder.Build();
host.Run();
