using OtelDemo.Notifications;
using OtelDemo.Shared;

var builder = Host.CreateApplicationBuilder(args);
builder.AddOpenTelemetry("OtelDemo.Notifications");
builder.AddServiceBus(Constants.NotificationsQueue, senderNames: Constants.AuditTopic);
builder.Services.AddOptions<MessageSubscriberSettings>().Bind(builder.Configuration.GetSection("MessageSubscriberSettings"));
builder.Services.AddHostedService<NotificationsSubscriber>();
builder.Services.AddHttpClient();

var host = builder.Build();
host.Run();
