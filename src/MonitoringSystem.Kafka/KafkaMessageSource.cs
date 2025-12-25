using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonitoringSystem.Core.Abstractions;
using MonitoringSystem.Core.Domain;
using MonitoringSystem.Kafka.Configuration;

namespace MonitoringSystem.Kafka;

/// <summary>
/// Kafka implementation of IMessageSource for receiving sensor data.
/// </summary>
public sealed class KafkaMessageSource : IMessageSource, IDisposable
{
    private readonly IConsumer<string, SensorData> _consumer;
    private readonly ILogger<KafkaMessageSource> _logger;
    private readonly KafkaOptions _options;
    private readonly SemaphoreSlim _consumeLock = new(1, 1);
    private bool _disposed;
    private bool _subscribed;

    public KafkaMessageSource(
        IConsumer<string, SensorData> consumer,
        IOptions<KafkaOptions> options,
        ILogger<KafkaMessageSource> logger)
    {
        _consumer = consumer;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Message<SensorData>?> ReceiveAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return null;
        }

        await _consumeLock.WaitAsync(cancellationToken);
        try
        {
            EnsureSubscribed();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Consume with timeout to allow cancellation checks
                    var consumeResult = _consumer.Consume(
                        TimeSpan.FromMilliseconds(_options.ConsumeTimeoutMs));

                    if (consumeResult == null)
                    {
                        // No message available, continue waiting
                        continue;
                    }

                    if (consumeResult.IsPartitionEOF)
                    {
                        _logger.LogDebug(
                            "Reached end of partition {Partition} at offset {Offset}",
                            consumeResult.Partition,
                            consumeResult.Offset);
                        continue;
                    }

                    _logger.LogDebug(
                        "Received message for sensor {SensorId} from partition {Partition}",
                        consumeResult.Message.Value.SensorId,
                        consumeResult.Partition);

                    return new KafkaMessage(consumeResult, _consumer, _consumeLock);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Error consuming message: {Reason}", ex.Error.Reason);

                    if (ex.Error.IsFatal)
                    {
                        // Fatal error - source is done
                        return null;
                    }

                    // Transient error - continue
                    await Task.Delay(100, cancellationToken);
                }
            }

            return null;
        }
        finally
        {
            _consumeLock.Release();
        }
    }

    private void EnsureSubscribed()
    {
        if (_subscribed)
        {
            return;
        }

        _consumer.Subscribe(_options.Topic);
        _subscribed = true;
        _logger.LogInformation("Subscribed to topic {Topic}", _options.Topic);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _consumer.Close();
        _consumeLock.Dispose();
        _disposed = true;
    }
}
