using Confluent.Kafka;
using MonitoringSystem.Core.Abstractions;
using MonitoringSystem.Core.Domain;

namespace MonitoringSystem.Kafka;

/// <summary>
/// Kafka-specific implementation of Message{SensorData}.
/// Wraps a ConsumeResult and provides ack/nack via offset commit control.
/// Thread-safe: all consumer operations are synchronized via the provided lock.
/// </summary>
public sealed class KafkaMessage : Message<SensorData>
{
    private readonly ConsumeResult<string, SensorData> _consumeResult;
    private readonly IConsumer<string, SensorData> _consumer;
    private readonly SemaphoreSlim _consumerLock;
    private readonly Func<ConsumeResult<string, SensorData>, Task>? _onNackRequeue;
    private bool _acknowledged;

    public KafkaMessage(
        ConsumeResult<string, SensorData> consumeResult,
        IConsumer<string, SensorData> consumer,
        SemaphoreSlim consumerLock,
        Func<ConsumeResult<string, SensorData>, Task>? onNackRequeue = null)
    {
        _consumeResult = consumeResult;
        _consumer = consumer;
        _consumerLock = consumerLock;
        _onNackRequeue = onNackRequeue;
    }

    /// <inheritdoc />
    public override SensorData Data => _consumeResult.Message.Value;

    /// <summary>
    /// Gets the topic, partition, and offset information for this message.
    /// </summary>
    public TopicPartitionOffset TopicPartitionOffset => _consumeResult.TopicPartitionOffset;

    /// <summary>
    /// Stores the offset without committing. Use for batch commit scenarios.
    /// Call AckAsync() once after storing all offsets to commit the batch.
    /// </summary>
    public async Task StoreOffsetOnlyAsync()
    {
        if (_acknowledged)
        {
            return;
        }

        await _consumerLock.WaitAsync();
        try
        {
            _consumer.StoreOffset(_consumeResult);
        }
        finally
        {
            _consumerLock.Release();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Commits all stored offsets. For batch processing, call StoreOffsetOnlyAsync()
    /// on all messages first, then call AckAsync() once on any message to commit all.
    /// </remarks>
    public override async Task AckAsync()
    {
        if (_acknowledged)
        {
            return;
        }

        await _consumerLock.WaitAsync();
        try
        {
            // Commit all stored offsets at once (batch commit)
            _consumer.Commit();
            _acknowledged = true;
        }
        finally
        {
            _consumerLock.Release();
        }
    }

    /// <inheritdoc />
    public override async Task NackAsync(bool requeue)
    {
        if (_acknowledged)
        {
            return;
        }

        await _consumerLock.WaitAsync();
        try
        {
            if (requeue)
            {
                // Requeue: seek back to this offset for immediate retry
                if (_onNackRequeue != null)
                {
                    await _onNackRequeue(_consumeResult);
                }
                _consumer.Seek(_consumeResult.TopicPartitionOffset);
            }
            else
            {
                // Don't requeue: commit offset to skip this message (drop it)
                _consumer.StoreOffset(_consumeResult);
                _consumer.Commit(_consumeResult);
            }

            _acknowledged = true;
        }
        finally
        {
            _consumerLock.Release();
        }
    }
}
