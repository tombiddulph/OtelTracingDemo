# Otel demo

This is a demo project to show how to use OpenTelemetry with dotnet and service bus.
There are 5 projects in this solution:

- OtelDemo.Api: The main project that sends messages to  service bus viat an HTTP request.
- OtelDemo.Apples: A project that listens to the service bus and logs the messages.
- OtelDemo.Kiwis: A project that listens to the service bus and logs the messages.
- OtelDemo.Pears: A project that listens to the service bus and logs the messages.
- OtelDemo.Peaches: A project that listens to the service bus and logs the messages.

The other containers in the solution are:

- Aspire dashboard: A container that runs the Aspire dashboards.
- Service bus emulator: A container that runs the Azure Service Bus emulator.

## How to run

1. Clone the repository
2. Run the following command to start the projects/emulator:

```bash
docker compose up
```

It takes a few seconds to start the emulator. The api project starts with the emulator, and the rest of the projects will start when the api becomes healthy (emulator is ready).

Sample request:

```curl
curl --request POST \
  --url http://localhost:8080/start-process \
  --header 'content-type: application/json' \
  --data '{
  "UseLinks": true #if true use Activity links otherwise use the remote context as parent
  "NumberOfMessages": 1 #Number of messages to send
  "StartNewTrace": true # If true, start a new trace for each message, otherwise use the http request trace as parent
}'
```
