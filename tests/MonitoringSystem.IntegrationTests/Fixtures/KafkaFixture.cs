using Confluent.Kafka;
using MonitoringSystem.Core.Domain;
using MonitoringSystem.Kafka.Serialization;
using Testcontainers.Kafka;
using Xunit;

namespace MonitoringSystem.IntegrationTests.Fixtures;

/// <summary>
/// Fixture that manages a Kafka container for integration testing.
/// </summary>
public sealed class KafkaFixture : IAsyncLifetime
{
    private KafkaContainer? _container;

    /// <summary>
    /// Gets the bootstrap servers address for connecting to Kafka.
    /// </summary>
    public string BootstrapServers => _container?.GetBootstrapAddress()
        ?? throw new InvalidOperationException("Container not started");

    public async Task InitializeAsync()
    {
        _container = new KafkaBuilder()
            .WithImage("confluentinc/cp-kafka:7.5.0")
            .Build();

        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// Creates a producer for sending string key/value messages.
    /// </summary>
    public IProducer<string, string> CreateProducer()
    {
        var config = new ProducerConfig
        {
            BootstrapServers = BootstrapServers
        };
        return new ProducerBuilder<string, string>(config).Build();
    }

    /// <summary>
    /// Creates a consumer for receiving SensorData messages.
    /// </summary>
    public IConsumer<string, SensorData> CreateSensorDataConsumer(string groupId)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = BootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false
        };

        return new ConsumerBuilder<string, SensorData>(config)
            .SetValueDeserializer(new SensorDataSerializer())
            .Build();
    }

    /// <summary>
    /// Creates a consumer for receiving string key/value messages.
    /// </summary>
    public IConsumer<string, string> CreateConsumer(string groupId)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = BootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };
        return new ConsumerBuilder<string, string>(config).Build();
    }
}
