using OtelDemo.Inventory;
using OtelDemo.Shared;

var builder = Host.CreateApplicationBuilder(args);
builder.AddOpenTelemetry("OtelDemo.Inventory");
builder.AddServiceBus(Constants.InventoryQueue, senderNames: [Constants.PaymentsQueue, Constants.AuditTopic]);
builder.Services.AddOptions<MessageSubscriberSettings>().Bind(builder.Configuration.GetSection("MessageSubscriberSettings"));
builder.Services.AddHostedService<InventorySubscriber>();

var host = builder.Build();
host.Run();
