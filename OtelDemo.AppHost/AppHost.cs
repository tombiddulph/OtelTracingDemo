var builder = DistributedApplication.CreateBuilder(args);

const string serviceBusConnectionString = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

var sqlPassword = builder.AddParameter("sql-password", "Pass@w0rd1!", secret: true);

var sqlServer = builder.AddContainer("sqlserver", "mcr.microsoft.com/mssql/server", "latest")
    .WithEnvironment("ACCEPT_EULA", "Y")
    .WithEnvironment("MSSQL_SA_PASSWORD", sqlPassword)
    .WithEndpoint(port: 1433, targetPort: 1433, name: "tcp");

var serviceBus = builder.AddContainer("servicebus-emulator", "mcr.microsoft.com/azure-messaging/servicebus-emulator", "latest")
    .WithBindMount("../servicebus-config.json", "/ServiceBus_Emulator/ConfigFiles/Config.json", isReadOnly: true)
    .WithEnvironment("ACCEPT_EULA", "y")
    .WithEnvironment("MSSQL_SA_PASSWORD", sqlPassword)
    .WithEnvironment("SQL_SERVER", "sqlserver")
    .WithEndpoint(port: 5672, targetPort: 5672, name: "amqp")
    .WaitFor(sqlServer);

builder.AddOpenTelemetryCollector("opentelemetry-collector", settings =>
    {
        settings.Registry = "docker.io";
        settings.Image = "otel/opentelemetry-collector-contrib";
        settings.CollectorTag = "latest";
    })
    .WithConfig("collector-config.yaml")
    .WithEnvironment("ASPIRE_ENDPOINT", "host.docker.internal:18889")
    .WithAppForwarding();

var api = builder.AddProject<Projects.OtelDemo_Api>("oteldemo-api")
    .WithEnvironment("ServiceBus__Namespace", serviceBusConnectionString)
    .WaitFor(serviceBus)
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.OtelDemo_Orders>("oteldemo-orders")
    .WithEnvironment("ServiceBus__Namespace", serviceBusConnectionString)
    .WaitFor(api);

builder.AddProject<Projects.OtelDemo_Inventory>("oteldemo-inventory")
    .WithEnvironment("ServiceBus__Namespace", serviceBusConnectionString)
    .WaitFor(api);

builder.AddProject<Projects.OtelDemo_Payments>("oteldemo-payments")
    .WithEnvironment("ServiceBus__Namespace", serviceBusConnectionString)
    .WaitFor(api);

builder.AddProject<Projects.OtelDemo_Notifications>("oteldemo-notifications")
    .WithEnvironment("ServiceBus__Namespace", serviceBusConnectionString)
    .WaitFor(api);

builder.AddProject<Projects.OtelDemo_Audit>("oteldemo-audit")
    .WithEnvironment("ServiceBus__Namespace", serviceBusConnectionString)
    .WaitFor(api);

builder.Build().Run();
