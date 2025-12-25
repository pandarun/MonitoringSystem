using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using MonitoringSystem.Core.Domain;
using MonitoringSystem.Data;
using MonitoringSystem.Kafka;
using MonitoringSystem.Kafka.Configuration;
using MonitoringSystem.Kafka.Serialization;
using MonitoringSystem.Worker;
using MonitoringSystem.Worker.Processing;

var builder = Host.CreateApplicationBuilder(args);

// Add service defaults (OpenTelemetry, health checks, etc.)
builder.AddServiceDefaults();

// Configure Kafka options
builder.Services.Configure<KafkaOptions>(
    builder.Configuration.GetSection(KafkaOptions.SectionName));

// Configure processing options
builder.Services.Configure<ProcessingOptions>(
    builder.Configuration.GetSection(ProcessingOptions.SectionName));

// Add Kafka consumer via Aspire integration
builder.AddKafkaConsumer<string, SensorData>("kafka", configureSettings: settings =>
{
    var kafkaSection = builder.Configuration.GetSection(KafkaOptions.SectionName);
    settings.Config.GroupId = kafkaSection.GetValue<string>("GroupId") ?? "sensor-processor-group";
    settings.Config.EnableAutoCommit = false;
    settings.Config.AutoOffsetReset = AutoOffsetReset.Earliest;
    settings.Config.EnableAutoOffsetStore = false;
}, configureBuilder: consumerBuilder =>
{
    consumerBuilder.SetValueDeserializer(new SensorDataSerializer());
});

// Add PostgreSQL with EF Core DbContextFactory for singleton-safe access
var connectionString = builder.Configuration.GetConnectionString("postgresdb");
builder.Services.AddDbContextFactory<MonitoringDbContext>(options =>
    options.UseNpgsql(connectionString));

// Register services using extension methods
builder.Services.AddKafkaMessageSource();
builder.Services.AddSensorDataDestination();
builder.Services.AddSingleton<BatchProcessor>();

// Add the processor background service
builder.Services.AddHostedService<SensorProcessor>();

var host = builder.Build();

// Ensure database is created and migrated
var dbContextFactory = host.Services.GetRequiredService<IDbContextFactory<MonitoringDbContext>>();
await using (var dbContext = await dbContextFactory.CreateDbContextAsync())
{
    await dbContext.Database.EnsureCreatedAsync();
}

await host.RunAsync();
