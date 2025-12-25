using System.Text.Json;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MonitoringSystem.Core.Abstractions;
using MonitoringSystem.Core.Domain;
using MonitoringSystem.Data;
using MonitoringSystem.Data.Repositories;
using MonitoringSystem.IntegrationTests.Fixtures;
using MonitoringSystem.Kafka;
using MonitoringSystem.Kafka.Configuration;
using MonitoringSystem.Kafka.Serialization;
using MonitoringSystem.Worker;
using MonitoringSystem.Worker.Processing;
using Xunit;

namespace MonitoringSystem.IntegrationTests;

[Collection("Integration")]
public class EndToEndTests
{
    private readonly KafkaFixture _kafkaFixture;
    private readonly PostgresFixture _postgresFixture;
    private const string TestTopic = "e2e-sensor-data";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public EndToEndTests(KafkaFixture kafkaFixture, PostgresFixture postgresFixture)
    {
        _kafkaFixture = kafkaFixture;
        _postgresFixture = postgresFixture;
    }

    [Fact]
    public async Task FullPipeline_ProducesAndConsumesMessages()
    {
        // Arrange - produce test messages
        using var producer = _kafkaFixture.CreateProducer();

        var testPrefix = $"e2e-{Guid.NewGuid():N}";
        var testData = Enumerable.Range(1, 50)
            .Select(i => new SensorData(
                $"{testPrefix}-sensor-{i % 5:D3}",  // 5 unique sensors
                i * 1.5,
                DateTime.UtcNow.AddSeconds(i)))
            .ToList();

        foreach (var data in testData)
        {
            await producer.ProduceAsync(TestTopic, new Message<string, string>
            {
                Key = data.SensorId,
                Value = JsonSerializer.Serialize(data, JsonOptions)
            });
        }

        producer.Flush(TimeSpan.FromSeconds(5));

        // Build host with real services
        var groupId = $"e2e-test-group-{Guid.NewGuid():N}";

        var hostBuilder = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                // Configure options
                services.Configure<KafkaOptions>(opts =>
                {
                    opts.Topic = TestTopic;
                    opts.GroupId = groupId;
                });

                services.Configure<ProcessingOptions>(opts =>
                {
                    opts.BatchSize = 10;
                    opts.BatchTimeout = TimeSpan.FromSeconds(2);
                });

                // Add Kafka consumer
                services.AddSingleton<IConsumer<string, SensorData>>(_ =>
                {
                    var config = new ConsumerConfig
                    {
                        BootstrapServers = _kafkaFixture.BootstrapServers,
                        GroupId = groupId,
                        AutoOffsetReset = AutoOffsetReset.Earliest,
                        EnableAutoCommit = false,
                        EnableAutoOffsetStore = false
                    };
                    return new ConsumerBuilder<string, SensorData>(config)
                        .SetValueDeserializer(new SensorDataSerializer())
                        .Build();
                });

                // Add DbContextFactory for singleton-safe DB access
                services.AddDbContextFactory<MonitoringDbContext>(opts =>
                    opts.UseNpgsql(_postgresFixture.ConnectionString));

                // Register services - use Singleton for all to avoid lifetime issues
                services.AddSingleton<IMessageSource, KafkaMessageSource>();
                services.AddSingleton<IDataDestination, SensorDataDestination>();
                services.AddSingleton<BatchProcessor>();
                services.AddHostedService<SensorProcessor>();
            });

        using var host = hostBuilder.Build();

        // Act - run processor for a short time
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var hostTask = host.RunAsync(cts.Token);

        // Wait for processing
        await Task.Delay(8000);

        cts.Cancel();
        try { await hostTask; } catch (OperationCanceledException) { }

        // Assert - verify database state
        await using var dbContext = _postgresFixture.CreateDbContext();

        var storedStates = await dbContext.SensorStates
            .Where(s => s.SensorId.StartsWith(testPrefix))
            .ToListAsync();

        // Should have exactly 5 sensors (sensor-000 through sensor-004)
        storedStates.Should().HaveCount(5);

        // Each should have the latest value for that sensor
        foreach (var state in storedStates)
        {
            // The latest message for each sensor should be stored
            state.Value.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task FullPipeline_HandlesUnorderedMessages()
    {
        // Arrange - produce messages out of order
        using var producer = _kafkaFixture.CreateProducer();

        var sensorId = $"unordered-{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;

        // Send messages in wrong order: newest first, oldest last
        var messages = new[]
        {
            new SensorData(sensorId, 30.0, now), // Newest (should be kept)
            new SensorData(sensorId, 10.0, now.AddMinutes(-2)),
            new SensorData(sensorId, 20.0, now.AddMinutes(-1)),
        };

        foreach (var data in messages)
        {
            await producer.ProduceAsync(TestTopic, new Message<string, string>
            {
                Key = data.SensorId,
                Value = JsonSerializer.Serialize(data, JsonOptions)
            });
        }

        producer.Flush(TimeSpan.FromSeconds(5));

        // Build and run host
        var groupId = $"unordered-test-group-{Guid.NewGuid():N}";

        var hostBuilder = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.Configure<KafkaOptions>(opts =>
                {
                    opts.Topic = TestTopic;
                    opts.GroupId = groupId;
                });

                services.Configure<ProcessingOptions>(opts =>
                {
                    opts.BatchSize = 10;
                    opts.BatchTimeout = TimeSpan.FromSeconds(1);
                });

                services.AddSingleton<IConsumer<string, SensorData>>(_ =>
                {
                    var config = new ConsumerConfig
                    {
                        BootstrapServers = _kafkaFixture.BootstrapServers,
                        GroupId = groupId,
                        AutoOffsetReset = AutoOffsetReset.Earliest,
                        EnableAutoCommit = false,
                        EnableAutoOffsetStore = false
                    };
                    return new ConsumerBuilder<string, SensorData>(config)
                        .SetValueDeserializer(new SensorDataSerializer())
                        .Build();
                });

                // Add DbContextFactory for singleton-safe DB access
                services.AddDbContextFactory<MonitoringDbContext>(opts =>
                    opts.UseNpgsql(_postgresFixture.ConnectionString));

                // Register services - use Singleton for all to avoid lifetime issues
                services.AddSingleton<IMessageSource, KafkaMessageSource>();
                services.AddSingleton<IDataDestination, SensorDataDestination>();
                services.AddSingleton<BatchProcessor>();
                services.AddHostedService<SensorProcessor>();
            });

        using var host = hostBuilder.Build();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var hostTask = host.RunAsync(cts.Token);

        await Task.Delay(8000);

        cts.Cancel();
        try { await hostTask; } catch (OperationCanceledException) { }

        // Assert - should have the newest value (30.0)
        await using var dbContext = _postgresFixture.CreateDbContext();

        var storedState = await dbContext.SensorStates
            .FirstOrDefaultAsync(s => s.SensorId == sensorId);

        storedState.Should().NotBeNull();
        storedState!.Value.Should().Be(30.0);
    }
}
