using OtelDemo.Payments;
using OtelDemo.Shared;

var builder = Host.CreateApplicationBuilder(args);
builder.AddOpenTelemetry("OtelDemo.Payments");
builder.AddServiceBus(Constants.PaymentsQueue, senderNames: [Constants.NotificationsQueue, Constants.AuditTopic]);
builder.Services.AddOptions<MessageSubscriberSettings>().Bind(builder.Configuration.GetSection("MessageSubscriberSettings"));
builder.Services.AddHostedService<PaymentsSubscriber>();

var host = builder.Build();
host.Run();
