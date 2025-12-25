var builder = DistributedApplication.CreateBuilder(args);

// Add PostgreSQL with persistent storage
var postgres = builder.AddPostgres("postgres")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("monitoring-postgres-data");

var database = postgres.AddDatabase("postgresdb");

// Add Kafka with UI for debugging
var kafka = builder.AddKafka("kafka")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("monitoring-kafka-data")
    .WithKafkaUI();

// Add the sensor data producer (demo/testing)
var producer = builder.AddProject<Projects.MonitoringSystem_Producer>("producer")
    .WithReference(kafka)
    .WaitFor(kafka);

// Add the sensor processor worker
var worker = builder.AddProject<Projects.MonitoringSystem_Worker>("worker")
    .WithReference(kafka)
    .WithReference(database)
    .WaitFor(kafka)
    .WaitFor(database);

builder.Build().Run();
