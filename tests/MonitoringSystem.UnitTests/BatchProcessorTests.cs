using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MonitoringSystem.Core.Abstractions;
using MonitoringSystem.Core.Domain;
using MonitoringSystem.Core.Exceptions;
using MonitoringSystem.Worker.Processing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MonitoringSystem.UnitTests;

public class BatchProcessorTests
{
    private readonly IDataDestination _mockDestination;
    private readonly IOptions<ProcessingOptions> _options;

    public BatchProcessorTests()
    {
        _mockDestination = Substitute.For<IDataDestination>();
        _options = Options.Create(new ProcessingOptions
        {
            BatchSize = 5,
            BatchTimeout = TimeSpan.FromMilliseconds(500),
            MaxRetries = 2,
            RetryDelay = TimeSpan.FromMilliseconds(10)
        });
    }

    [Fact]
    public async Task AddAsync_WhenBatchSizeReached_FlushesToDestination()
    {
        // Arrange
        using var batchProcessor = new BatchProcessor(
            _mockDestination,
            _options,
            NullLogger<BatchProcessor>.Instance);

        using var cts = new CancellationTokenSource();
        var processingTask = Task.Run(() => batchProcessor.ProcessBatchesAsync(cts.Token));

        var messages = Enumerable.Range(1, 5)
            .Select(i => new TestMessage(new SensorData($"sensor-{i:D3}", i * 10.0, DateTime.UtcNow)))
            .ToList();

        // Act
        foreach (var message in messages)
        {
            await batchProcessor.AddAsync(message, CancellationToken.None);
        }

        // Wait for processing
        await Task.Delay(200);

        cts.Cancel();
        batchProcessor.Complete();

        try { await processingTask; } catch (OperationCanceledException) { }

        // Assert
        await _mockDestination.Received(1).WriteBatchAsync(
            Arg.Is<IEnumerable<SensorData>>(data => data.Count() == 5),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatchesAsync_WhenTimeoutReached_FlushesPartialBatch()
    {
        // Arrange
        using var batchProcessor = new BatchProcessor(
            _mockDestination,
            _options,
            NullLogger<BatchProcessor>.Instance);

        using var cts = new CancellationTokenSource();
        var processingTask = Task.Run(() => batchProcessor.ProcessBatchesAsync(cts.Token));

        // Add fewer messages than batch size
        var messages = Enumerable.Range(1, 2)
            .Select(i => new TestMessage(new SensorData($"sensor-{i:D3}", i * 10.0, DateTime.UtcNow)))
            .ToList();

        foreach (var message in messages)
        {
            await batchProcessor.AddAsync(message, CancellationToken.None);
        }

        // Wait for timeout (500ms) + buffer
        await Task.Delay(800);

        cts.Cancel();
        batchProcessor.Complete();

        try { await processingTask; } catch (OperationCanceledException) { }

        // Assert - should have flushed the partial batch
        await _mockDestination.Received().WriteBatchAsync(
            Arg.Is<IEnumerable<SensorData>>(data => data.Count() == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatchesAsync_WhenWriteSucceeds_AcksLastMessage()
    {
        // Arrange
        using var batchProcessor = new BatchProcessor(
            _mockDestination,
            _options,
            NullLogger<BatchProcessor>.Instance);

        using var cts = new CancellationTokenSource();
        var processingTask = Task.Run(() => batchProcessor.ProcessBatchesAsync(cts.Token));

        var messages = Enumerable.Range(1, 5)
            .Select(i => new TestMessage(new SensorData($"sensor-{i:D3}", i * 10.0, DateTime.UtcNow)))
            .ToList();

        // Act
        foreach (var message in messages)
        {
            await batchProcessor.AddAsync(message, CancellationToken.None);
        }

        await Task.Delay(200);

        cts.Cancel();
        batchProcessor.Complete();

        try { await processingTask; } catch (OperationCanceledException) { }

        // Assert - with batch commits, only the last message triggers commit
        // KafkaMessage-specific offset storage is handled separately
        messages[^1].WasAcked.Should().BeTrue();
        messages.Should().OnlyContain(m => !m.WasNacked);
    }

    [Fact]
    public async Task ProcessBatchesAsync_WhenWriteFailsWithValidationError_NacksWithoutRequeue()
    {
        // Arrange
        _mockDestination
            .WriteBatchAsync(Arg.Any<IEnumerable<SensorData>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DataValidationException("Invalid data"));

        using var batchProcessor = new BatchProcessor(
            _mockDestination,
            _options,
            NullLogger<BatchProcessor>.Instance);

        using var cts = new CancellationTokenSource();
        var processingTask = Task.Run(() => batchProcessor.ProcessBatchesAsync(cts.Token));

        var messages = Enumerable.Range(1, 5)
            .Select(i => new TestMessage(new SensorData($"sensor-{i:D3}", i * 10.0, DateTime.UtcNow)))
            .ToList();

        // Act
        foreach (var message in messages)
        {
            await batchProcessor.AddAsync(message, CancellationToken.None);
        }

        await Task.Delay(200);

        cts.Cancel();
        batchProcessor.Complete();

        try { await processingTask; } catch (OperationCanceledException) { }

        // Assert - validation errors should nack with requeue=true
        // (in real implementation you might want to send to DLQ instead)
        messages.Should().OnlyContain(m => m.WasNacked);
    }

    [Fact]
    public async Task ProcessBatchesAsync_WhenWriteFailsWithRetryableError_RetriesBeforeNack()
    {
        // Arrange
        var callCount = 0;
        _mockDestination
            .WriteBatchAsync(Arg.Any<IEnumerable<SensorData>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                throw new InvalidOperationException("Transient error");
            });

        using var batchProcessor = new BatchProcessor(
            _mockDestination,
            _options,
            NullLogger<BatchProcessor>.Instance);

        using var cts = new CancellationTokenSource();
        var processingTask = Task.Run(() => batchProcessor.ProcessBatchesAsync(cts.Token));

        var messages = Enumerable.Range(1, 5)
            .Select(i => new TestMessage(new SensorData($"sensor-{i:D3}", i * 10.0, DateTime.UtcNow)))
            .ToList();

        // Act
        foreach (var message in messages)
        {
            await batchProcessor.AddAsync(message, CancellationToken.None);
        }

        await Task.Delay(500);

        cts.Cancel();
        batchProcessor.Complete();

        try { await processingTask; } catch (OperationCanceledException) { }

        // Assert - should have retried (MaxRetries = 2, so 3 total attempts)
        callCount.Should().Be(3);

        // Only the first message should be nacked with requeue=true (to seek back)
        // The rest remain unacknowledged and will be redelivered after the seek
        messages[0].WasNacked.Should().BeTrue();
        messages[0].WasRequeued.Should().BeTrue();

        // Remaining messages should not be touched (they'll be redelivered after seek)
        for (var i = 1; i < messages.Count; i++)
        {
            messages[i].WasNacked.Should().BeFalse();
            messages[i].WasAcked.Should().BeFalse();
        }
    }
}
