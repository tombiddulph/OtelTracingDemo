using OtelDemo.Kiwis;
using OtelDemo.Shared;

var builder = Host.CreateApplicationBuilder(args);
builder.AddOpenTelemetry("OtelDemo.Kiwis");
builder.AddServiceBus("kiwis");
builder.Services.AddOptions<MessageSubscriberSettings>().Bind(builder.Configuration.GetSection("MessageSubscriberSettings"));
builder.Services.AddHostedService<KiwisSubscriber>();

var host = builder.Build();
host.Run();