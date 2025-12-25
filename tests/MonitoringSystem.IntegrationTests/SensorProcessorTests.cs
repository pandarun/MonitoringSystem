using System.Text.Json;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MonitoringSystem.Core.Domain;
using MonitoringSystem.Data.Repositories;
using MonitoringSystem.IntegrationTests.Fixtures;
using MonitoringSystem.Kafka.Serialization;
using MonitoringSystem.Worker.Processing;
using Xunit;

namespace MonitoringSystem.IntegrationTests;

[Collection("Integration")]
public class SensorProcessorTests
{
    private readonly KafkaFixture _kafkaFixture;
    private readonly PostgresFixture _postgresFixture;
    private const string TestTopic = "test-sensor-data";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SensorProcessorTests(KafkaFixture kafkaFixture, PostgresFixture postgresFixture)
    {
        _kafkaFixture = kafkaFixture;
        _postgresFixture = postgresFixture;
    }

    [Fact]
    public async Task WriteBatchAsync_WritesLatestStatePerSensor()
    {
        // Arrange
        var dbContextFactory = _postgresFixture.CreateDbContextFactory();
        var destination = new SensorDataDestination(
            dbContextFactory,
            NullLogger<SensorDataDestination>.Instance);

        var now = DateTime.UtcNow;

        // Multiple messages for same sensor (different timestamps)
        var sensorData = new[]
        {
            new SensorData("sensor-001", 10.0, now.AddMinutes(-2)),
            new SensorData("sensor-001", 20.0, now.AddMinutes(-1)),
            new SensorData("sensor-001", 30.0, now), // Latest
        };

        // Act
        await destination.WriteBatchAsync(sensorData, CancellationToken.None);

        // Assert - only latest value should be stored
        await using var dbContext = _postgresFixture.CreateDbContext();
        var storedState = await dbContext.SensorStates
            .FirstOrDefaultAsync(s => s.SensorId == "sensor-001");

        storedState.Should().NotBeNull();
        storedState!.Value.Should().Be(30.0);
    }

    [Fact]
    public async Task WriteBatchAsync_HandlesMultipleSensors()
    {
        // Arrange
        var dbContextFactory = _postgresFixture.CreateDbContextFactory();
        var destination = new SensorDataDestination(
            dbContextFactory,
            NullLogger<SensorDataDestination>.Instance);

        var now = DateTime.UtcNow;

        var sensorData = new[]
        {
            new SensorData("multi-sensor-001", 10.0, now),
            new SensorData("multi-sensor-002", 20.0, now),
            new SensorData("multi-sensor-003", 30.0, now),
        };

        // Act
        await destination.WriteBatchAsync(sensorData, CancellationToken.None);

        // Assert
        await using var dbContext = _postgresFixture.CreateDbContext();
        var storedStates = await dbContext.SensorStates
            .Where(s => s.SensorId.StartsWith("multi-sensor-"))
            .ToListAsync();

        storedStates.Should().HaveCount(3);
        storedStates.Should().Contain(s => s.SensorId == "multi-sensor-001" && s.Value == 10.0);
        storedStates.Should().Contain(s => s.SensorId == "multi-sensor-002" && s.Value == 20.0);
        storedStates.Should().Contain(s => s.SensorId == "multi-sensor-003" && s.Value == 30.0);
    }

    [Fact]
    public async Task WriteBatchAsync_OnlyUpdatesIfTimestampIsNewer()
    {
        // Arrange
        var dbContextFactory = _postgresFixture.CreateDbContextFactory();
        var destination = new SensorDataDestination(
            dbContextFactory,
            NullLogger<SensorDataDestination>.Instance);

        var now = DateTime.UtcNow;

        // Write initial data
        var initialData = new[] { new SensorData("timestamp-test", 100.0, now) };
        await destination.WriteBatchAsync(initialData, CancellationToken.None);

        // Try to write older data
        var olderData = new[] { new SensorData("timestamp-test", 50.0, now.AddMinutes(-5)) };
        await destination.WriteBatchAsync(olderData, CancellationToken.None);

        // Assert - should still have the newer value
        await using var dbContext = _postgresFixture.CreateDbContext();
        var storedState = await dbContext.SensorStates
            .FirstOrDefaultAsync(s => s.SensorId == "timestamp-test");

        storedState.Should().NotBeNull();
        storedState!.Value.Should().Be(100.0); // Original value, not the older one
    }

    [Fact]
    public async Task Kafka_ProducerConsumer_RoundTrip()
    {
        // Arrange
        using var producer = _kafkaFixture.CreateProducer();
        var testSensorId = $"kafka-test-{Guid.NewGuid():N}";
        var sensorData = new SensorData(testSensorId, 42.5, DateTime.UtcNow);

        // Act - produce message
        await producer.ProduceAsync(TestTopic, new Message<string, string>
        {
            Key = sensorData.SensorId,
            Value = JsonSerializer.Serialize(sensorData, JsonOptions)
        });
        producer.Flush(TimeSpan.FromSeconds(5));

        // Consume message
        using var consumer = _kafkaFixture.CreateConsumer($"test-group-{Guid.NewGuid():N}");
        consumer.Subscribe(TestTopic);

        var consumeResult = consumer.Consume(TimeSpan.FromSeconds(10));

        // Assert
        consumeResult.Should().NotBeNull();
        consumeResult.Message.Key.Should().Be(testSensorId);

        var deserializedData = JsonSerializer.Deserialize<SensorData>(
            consumeResult.Message.Value, JsonOptions);
        deserializedData.Should().NotBeNull();
        deserializedData!.SensorId.Should().Be(testSensorId);
        deserializedData.Value.Should().Be(42.5);
    }

    [Fact]
    public async Task BatchProcessor_FlushesOnTimeout()
    {
        // Arrange
        var dbContextFactory = _postgresFixture.CreateDbContextFactory();
        var destination = new SensorDataDestination(
            dbContextFactory,
            NullLogger<SensorDataDestination>.Instance);

        var options = Options.Create(new ProcessingOptions
        {
            BatchSize = 100, // Large batch size
            BatchTimeout = TimeSpan.FromMilliseconds(500) // Short timeout
        });

        using var batchProcessor = new BatchProcessor(
            destination,
            options,
            NullLogger<BatchProcessor>.Instance);

        using var cts = new CancellationTokenSource();
        var processingTask = Task.Run(() => batchProcessor.ProcessBatchesAsync(cts.Token));

        // Act - add just a few messages (less than batch size)
        var sensorId = $"timeout-test-{Guid.NewGuid():N}";
        var testMessage = new TestMessage(new SensorData(sensorId, 42.0, DateTime.UtcNow));

        await batchProcessor.AddAsync(testMessage, CancellationToken.None);

        // Wait for timeout to trigger flush
        await Task.Delay(1000);

        cts.Cancel();
        batchProcessor.Complete();

        try { await processingTask; } catch (OperationCanceledException) { }

        // Assert - data should be written despite not reaching batch size
        await using var dbContext = _postgresFixture.CreateDbContext();
        var storedState = await dbContext.SensorStates
            .FirstOrDefaultAsync(s => s.SensorId == sensorId);

        storedState.Should().NotBeNull();
        storedState!.Value.Should().Be(42.0);
    }

    private sealed class TestMessage : Core.Abstractions.Message<SensorData>
    {
        private readonly SensorData _data;

        public TestMessage(SensorData data) => _data = data;

        public override SensorData Data => _data;
        public override Task AckAsync() => Task.CompletedTask;
        public override Task NackAsync(bool requeue) => Task.CompletedTask;
    }
}
