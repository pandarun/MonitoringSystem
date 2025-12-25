using MonitoringSystem.Producer;

var builder = Host.CreateApplicationBuilder(args);

// Add service defaults (OpenTelemetry, health checks, etc.)
builder.AddServiceDefaults();

// Add Kafka producer via Aspire integration
builder.AddKafkaProducer<string, string>("kafka");

// Add producer service
builder.Services.AddHostedService<SensorDataProducer>();

var host = builder.Build();
await host.RunAsync();
