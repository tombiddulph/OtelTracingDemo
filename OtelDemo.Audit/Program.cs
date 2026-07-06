using OtelDemo.Audit;
using OtelDemo.Shared;

var builder = Host.CreateApplicationBuilder(args);
builder.AddOpenTelemetry("OtelDemo.Audit");
builder.AddServiceBus(Constants.AuditTopic, Constants.AuditSubscription);
builder.Services.AddOptions<MessageSubscriberSettings>().Bind(builder.Configuration.GetSection("MessageSubscriberSettings"));
builder.Services.AddHostedService<AuditSubscriber>();

var host = builder.Build();
host.Run();
