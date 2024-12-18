using OtelDemo.Pears;
using OtelDemo.Shared;

var builder = Host.CreateApplicationBuilder(args);
builder.AddOpenTelemetry("OtelDemo.Pears");
builder.AddServiceBus("pears", senderNames:"kiwis");
builder.Services.AddOptions<MessageSubscriberSettings>().Bind(builder.Configuration.GetSection("MessageSubscriberSettings"));
builder.Services.AddHostedService<PearsSubscriber>();

var host = builder.Build();
host.Run();