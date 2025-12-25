using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MonitoringSystem.Core.Abstractions;
using MonitoringSystem.Worker.Processing;

namespace MonitoringSystem.Worker;

/// <summary>
/// Background service that consumes sensor data from Kafka and writes to database.
/// </summary>
public sealed class SensorProcessor : BackgroundService
{
    private readonly IMessageSource _messageSource;
    private readonly BatchProcessor _batchProcessor;
    private readonly ILogger<SensorProcessor> _logger;

    public SensorProcessor(
        IMessageSource messageSource,
        BatchProcessor batchProcessor,
        ILogger<SensorProcessor> logger)
    {
        _messageSource = messageSource;
        _batchProcessor = batchProcessor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SensorProcessor starting");

        // Start batch processing task
        var batchProcessingTask = Task.Run(
            () => _batchProcessor.ProcessBatchesAsync(stoppingToken),
            stoppingToken);

        try
        {
            await ConsumeMessagesAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("SensorProcessor shutting down");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in SensorProcessor");
            throw;
        }
        finally
        {
            // Signal completion to batch processor
            _batchProcessor.Complete();

            // Wait for batch processor to finish
            try
            {
                await batchProcessingTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }

            _logger.LogInformation("SensorProcessor stopped");
        }
    }

    private async Task ConsumeMessagesAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var message = await _messageSource.ReceiveAsync(stoppingToken);

            if (message == null)
            {
                _logger.LogInformation("Message source completed, no more messages expected");
                break;
            }

            _logger.LogDebug(
                "Received sensor data: {SensorId} = {Value} at {Timestamp}",
                message.Data.SensorId,
                message.Data.Value,
                message.Data.Timestamp);

            await _batchProcessor.AddAsync(message, stoppingToken);
        }
    }
}
