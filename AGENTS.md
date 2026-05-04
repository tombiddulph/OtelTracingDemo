# AGENTS.md - OpenTelemetry Distributed Tracing Demo

AI agents working on this codebase should understand this is a **distributed microservices demo showcasing OpenTelemetry tracing patterns** with Azure Service Bus. The project demonstrates different trace composition strategies (Activity Links vs parent context inheritance).

## Architecture & Data Flow

**Services (5 microservices + infrastructure)**:
- **OtelDemo.Api** (port 8080/8081): ASP.NET Slim WebAPI that initiates trace spans and publishes messages to Service Bus
- **OtelDemo.{Apples,Kiwis,Pears,Peaches}**: Identical background service subscribers listening to topics/queues
- **OtelDemo.Shared**: Configuration extensions and context propagation helpers
- **Infrastructure**: Service Bus emulator (sb://servicebus-emulator), SQL Server, OpenTelemetry Collector, Aspire Dashboard

**Message Flow**:
1. HTTP POST `/start-process` creates Activity in API, publishes to `api-topic` queue
2. Subscribers extract trace context from `ServiceBusMessage.ApplicationProperties` via W3C propagator
3. Each subscriber either:
   - **Links mode**: Creates new Activity and adds ActivityLink to extracted context
   - **Parent mode**: Uses extracted context as parent Activity
4. Collector receives OTLP traces (gRPC:4317) and exports to Aspire Dashboard (18889)

## Project-Specific Conventions

### 1. **ActivitySource Per Service** (NOT shared)
Each project defines its own ActivitySource in `ActivityConfig.cs`:
```csharp
public class ActivityConfig
{
    public static readonly ActivitySource Source = new("OtelDemo.Apples");
}
```
- **Why**: Allows filtering traces by service in Aspire Dashboard
- When adding a new service: Create`ActivityConfig.cs` with unique source name

### 2. **Context Propagation via ServiceBusMessage.ApplicationProperties**
The `ContextHelper` extension in `Shared/Context.cs` handles both directions:
- **Inject**: `message.InjectContext()` - copies Activity context to ApplicationProperties
- **Extract**: `source.CreateActivityFromMessage(message, useLinks)` - reconstructs Activity from properties

The `useLinks` parameter controls trace composition:
- `true`: Creates new Activity + ActivityLink (parallel traces visible in dashboard)
- `false`: Creates activity with extracted context as parent (linear trace)

### 3. **MessageSubscriberBase Pattern** (Abstract base for all consumers)
All subscriber services inherit from `MessageSubscriberBase`:
```csharp
public abstract class MessageSubscriberBase : BackgroundService
{
    public abstract Task HandleMessage(ProcessMessageEventArgs args, CancellationToken token);
}
```
- Handles processor lifecycle, error handling, max concurrent calls (50)
- Override `HandleMessage()` for custom logic
- Requires `MessageSubscriberSettings` in appsettings.json with ProcessorQueueOrTopicName, SubscriptionName, SenderName

### 4. **Configuration Structure**
Every service needs these config sections in `appsettings.json`:
```json
{
  "ServiceBus": { "Namespace": "Endpoint=sb://..." },
  "MessageSubscriberSettings": {
    "ProcessorQueueOrTopicName": "api-topic",
    "SubscriptionName": "apples-subscription",
    "SenderName": "mangoes"
  }
}
```

### 5. **HostApplicationBuilder Extensions** (Centralized setup)
`OtelDemo.Shared/HostApplicationBuilderExtensions.cs` provides:
- `AddServiceBus()`: Configures Azure clients (processor + senders)
- `AddOpenTelemetry()`: Configures OTLP exporter to collector, Azure instrumentation, metrics (http latency buckets), logs
- Every `Program.cs` calls both

### 6. **Trace Context in ApplicationProperties**
Service Bus doesn't natively propagate W3C trace headers, so custom logic injects them as ApplicationProperties:
```csharp
newMessage.InjectContext(); // Before sending
context = Propagators.DefaultTextMapPropagator.Extract(..., message.ApplicationProperties, ...); // When receiving
```
All subscribers must call `InjectContext()` before sending downstream messages.

## Critical Developer Workflows

### Running the Full Stack
```bash
cd C:\Users\tombi\Projects\OtelTracingDemo
docker compose up
# Wait 20-30s for emulator + API health check
```
- API becomes available: `http://localhost:8080`
- Aspire Dashboard: `http://localhost:18888` (traces appear here)
- Service Bus Emulator: `sb://localhost` (from containers)

### Testing Trace Composition
POST to `http://localhost:8080/start-process`:
```json
{
  "UseLinks": true,      // true=ActivityLink (parallel), false=parent tracing (linear)
  "NumberOfMessages": 1,
  "StartNewTrace": true, // true=new trace per message, false=reuse HTTP request trace
  "Batch": false         // true=batch messages, false=send individually
}
```

### Adding a New Subscriber Service
1. Create `OtelDemo.NewService/Program.cs`:
   ```csharp
   builder.AddOpenTelemetry("OtelDemo.NewService");
   builder.AddServiceBus("topic-name", "subscription-name", "sender-names...");
   builder.Services.AddHostedService<YourSubscriber>();
   ```
2. Create `ActivityConfig.cs` with unique ActivitySource name
3. Inherit subscriber from `MessageSubscriberBase`, implement `HandleMessage()`
4. Configure `appsettings.json` with ServiceBus + MessageSubscriberSettings
5. Add to `compose.yaml` with depends_on API healthcheck

### Building/Testing Local (without Docker)
```bash
dotnet build
# Requires Service Bus emulator connection string in appsettings
# OTLP collector must be available at http://localhost:4317
```

## Integration Points & External Dependencies

### Azure Service Bus
- **Emulator endpoint**: `Endpoint=sb://servicebus-emulator;...UseDevelopmentEmulator=true;` (Docker)
- **Queue/Topic setup**: Configured in `servicebus-config.json` mounted to emulator
- **API Design**: Queue processors default to 50 concurrent calls (`MaxConcurrentCalls`)

### OpenTelemetry Collector
- **Endpoint**: `http://oteldemo.collector:4317` (gRPC OTLP)
- **Config**: `collector/collector-config.yaml`
- **Exporters**: Aspire Dashboard + debug console output
- **Sampling**: Always-on sampler (no dropping of traces)

### Health Checks
- API `/health` endpoint waits for Service Bus connection
- Used by compose.yaml to delay subscriber startup until API is healthy
- Configure via `HealthCheckSettings` in appsettings

### Aspire Dashboard
- Reads OTLP traces at `http://aspire-dashboard:18889`
- Shows distributed trace waterfall, service topology, metrics
- No auth in dev setup (`DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true`)

## Debugging & Common Issues

**Traces not appearing in dashboard?**
- Verify `/health` returns 200 on API
- Check collector CPU/memory not saturated
- Logs show serialization errors? Debug `CreateActivityFromMessage()` context extraction

**Activity Links not visible?**
- Ensure `UseLinks=true` in request
- Dashboard must show span linked_to relationships
- Check Baggage propagation via ApplicationProperties

**Service Bus connection fails?**
- If local: Verify emulator container is running and connected to compose network
- Check `ServiceBus__Namespace` env var properly formatted
- For containers: Use service name `sb://servicebus-emulator` not localhost

## Files Worth Studying

- `OtelDemo.Shared/HostApplicationBuilderExtensions.cs` - Core OTLP + Azure setup
- `OtelDemo.Shared/Context.cs` - Trace context propagation logic
- `OtelDemo.Api/Program.cs` - Trace composition patterns (Activity Links vs parent)
- `compose.yaml` - Service orchestration, networking, health checks
- `collector/collector-config.yaml` - OTLP receiver → Aspire exporter pipeline

