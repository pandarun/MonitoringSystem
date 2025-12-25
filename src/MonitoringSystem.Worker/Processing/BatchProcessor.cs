using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonitoringSystem.Core.Abstractions;
using MonitoringSystem.Core.Domain;
using MonitoringSystem.Core.Exceptions;
using MonitoringSystem.Kafka;

namespace MonitoringSystem.Worker.Processing;

/// <summary>
/// Accumulates messages into batches for efficient database writes.
/// Handles batch timeout and error scenarios.
/// </summary>
public sealed class BatchProcessor : IDisposable
{
    private readonly IDataDestination _destination;
    private readonly ProcessingOptions _options;
    private readonly ILogger<BatchProcessor> _logger;
    private readonly Channel<Message<SensorData>> _channel;
    private readonly List<Message<SensorData>> _currentBatch;
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private DateTime _batchStartTime;
    private bool _disposed;

    public BatchProcessor(
        IDataDestination destination,
        IOptions<ProcessingOptions> options,
        ILogger<BatchProcessor> logger)
    {
        _destination = destination;
        _options = options.Value;
        _logger = logger;
        _channel = Channel.CreateBounded<Message<SensorData>>(
            new BoundedChannelOptions(_options.BatchSize * 2)
            {
                FullMode = BoundedChannelFullMode.Wait
            });
        _currentBatch = new List<Message<SensorData>>(_options.BatchSize);
        _batchStartTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Adds a message to the batch processing queue.
    /// </summary>
    public async Task AddAsync(Message<SensorData> message, CancellationToken cancellationToken)
    {
        await _channel.Writer.WriteAsync(message, cancellationToken);
    }

    /// <summary>
    /// Processes batches continuously until cancelled or channel is completed.
    /// </summary>
    public async Task ProcessBatchesAsync(CancellationToken cancellationToken)
    {
        var reader = _channel.Reader;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Calculate remaining timeout
                var elapsed = DateTime.UtcNow - _batchStartTime;
                var remainingTimeout = _options.BatchTimeout - elapsed;

                if (remainingTimeout <= TimeSpan.Zero)
                {
                    // Timeout reached, flush what we have
                    if (_currentBatch.Count > 0)
                    {
                        await FlushBatchAsync(cancellationToken);
                    }

                    _batchStartTime = DateTime.UtcNow;
                    continue;
                }

                // Wait for a message with timeout
                using var timeoutCts = new CancellationTokenSource(remainingTimeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, timeoutCts.Token);

                try
                {
                    var hasData = await reader.WaitToReadAsync(linkedCts.Token);

                    if (!hasData)
                    {
                        // Channel completed - no more messages will arrive
                        _logger.LogInformation("Channel completed, exiting batch processor");
                        break;
                    }

                    while (reader.TryRead(out var message))
                    {
                        _currentBatch.Add(message);

                        if (_currentBatch.Count >= _options.BatchSize)
                        {
                            await FlushBatchAsync(cancellationToken);
                            _batchStartTime = DateTime.UtcNow;
                        }
                    }
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    // Timeout - flush current batch
                    if (_currentBatch.Count > 0)
                    {
                        await FlushBatchAsync(cancellationToken);
                    }

                    _batchStartTime = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ChannelClosedException)
            {
                // Channel was closed - exit gracefully
                _logger.LogInformation("Channel closed, exiting batch processor");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in batch processing loop");
            }
        }

        // Final flush on shutdown
        if (_currentBatch.Count > 0)
        {
            await FlushBatchAsync(CancellationToken.None);
        }
    }

    private async Task FlushBatchAsync(CancellationToken cancellationToken)
    {
        if (_currentBatch.Count == 0)
        {
            return;
        }

        await _flushLock.WaitAsync(cancellationToken);
        try
        {
            var batch = _currentBatch.ToList();
            _currentBatch.Clear();

            var result = await TryWriteBatchWithRetryAsync(batch, cancellationToken);

            if (result == BatchWriteResult.Success)
            {
                // Store offsets for all messages, then commit once (batch commit)
                foreach (var message in batch)
                {
                    if (message is KafkaMessage kafkaMsg)
                    {
                        await kafkaMsg.StoreOffsetOnlyAsync();
                    }
                }

                // Single commit for entire batch
                if (batch.Count > 0)
                {
                    await batch[^1].AckAsync();
                }

                _logger.LogInformation("Flushed batch of {Count} messages", batch.Count);
            }
            else if (result == BatchWriteResult.ValidationError)
            {
                // Validation errors - nack without requeue (messages are invalid, don't retry)
                foreach (var message in batch)
                {
                    await message.NackAsync(requeue: false);
                }

                _logger.LogWarning("Dropped batch of {Count} messages due to validation errors", batch.Count);
            }
            else
            {
                // Transient errors - nack with requeue for retry
                // Only requeue the first message (earliest offset) - the rest will be redelivered after seek
                if (batch.Count > 0)
                {
                    await batch[0].NackAsync(requeue: true);
                }

                _logger.LogWarning("Nacked batch of {Count} messages for retry", batch.Count);
            }
        }
        finally
        {
            _flushLock.Release();
        }
    }

    private enum BatchWriteResult
    {
        Success,
        ValidationError,
        TransientError
    }

    private async Task<BatchWriteResult> TryWriteBatchWithRetryAsync(
        List<Message<SensorData>> batch,
        CancellationToken cancellationToken)
    {
        var data = batch.Select(m => m.Data).ToList();

        for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            try
            {
                await _destination.WriteBatchAsync(data, cancellationToken);
                return BatchWriteResult.Success;
            }
            catch (DataValidationException ex)
            {
                // Validation errors - don't retry, these won't succeed
                _logger.LogError(ex, "Validation error in batch: {Errors}",
                    string.Join(", ", ex.ValidationErrors));
                return BatchWriteResult.ValidationError;
            }
            catch (Exception ex) when (attempt < _options.MaxRetries)
            {
                _logger.LogWarning(ex,
                    "Batch write failed (attempt {Attempt}/{Max}), retrying...",
                    attempt + 1, _options.MaxRetries + 1);
                await Task.Delay(_options.RetryDelay, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Batch write failed after all retries");
                return BatchWriteResult.TransientError;
            }
        }

        return BatchWriteResult.TransientError;
    }

    /// <summary>
    /// Signals that no more messages will be added.
    /// </summary>
    public void Complete() => _channel.Writer.Complete();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _flushLock.Dispose();
        _disposed = true;
    }
}
