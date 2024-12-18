using OtelDemo.Peaches;
using OtelDemo.Shared;

var builder = Host.CreateApplicationBuilder(args);
builder.AddOpenTelemetry("OtelDemo.Peaches");
builder.AddServiceBus("peaches", senderNames:"mangoes");
builder.Services.AddOptions<MessageSubscriberSettings>().Bind(builder.Configuration.GetSection("MessageSubscriberSettings"));
builder.Services.AddHostedService<PeachesSubscriber>();

var host = builder.Build();
host.Run();