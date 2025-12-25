using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MonitoringSystem.Core.Abstractions;
using MonitoringSystem.Core.Domain;
using MonitoringSystem.Core.Exceptions;
using MonitoringSystem.Data.Entities;

namespace MonitoringSystem.Data.Repositories;

/// <summary>
/// PostgreSQL implementation of IDataDestination using EF Core.
/// Stores only the latest state per SensorId using upsert semantics.
/// </summary>
public sealed class SensorDataDestination : IDataDestination
{
    private readonly IDbContextFactory<MonitoringDbContext> _dbContextFactory;
    private readonly ILogger<SensorDataDestination> _logger;

    public SensorDataDestination(
        IDbContextFactory<MonitoringDbContext> dbContextFactory,
        ILogger<SensorDataDestination> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task WriteBatchAsync(
        IEnumerable<SensorData> payload,
        CancellationToken cancellationToken)
    {
        var dataList = payload.ToList();

        if (dataList.Count == 0)
        {
            return;
        }

        // Validate all data first
        var validationErrors = ValidatePayload(dataList);
        if (validationErrors.Count > 0)
        {
            throw new DataValidationException("Batch contains invalid sensor data")
            {
                ValidationErrors = validationErrors
            };
        }

        // Group by SensorId and take only the latest value for each
        var latestBySensor = dataList
            .GroupBy(d => d.SensorId)
            .Select(g => g.OrderByDescending(d => d.Timestamp).First())
            .ToList();

        var now = DateTime.UtcNow;

        // Use PostgreSQL upsert (INSERT ... ON CONFLICT ... DO UPDATE)
        // Wrap in transaction for all-or-nothing batch semantics
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var sensorData in latestBySensor)
            {
                var entity = new SensorStateEntity
                {
                    SensorId = sensorData.SensorId,
                    Value = sensorData.Value,
                    Timestamp = sensorData.Timestamp,
                    UpdatedAt = now
                };

                // Upsert: only update if incoming timestamp is newer
                await dbContext.Database.ExecuteSqlAsync(
                    $"""
                    INSERT INTO sensor_states (sensor_id, value, timestamp, updated_at)
                    VALUES ({entity.SensorId}, {entity.Value}, {entity.Timestamp}, {entity.UpdatedAt})
                    ON CONFLICT (sensor_id) DO UPDATE
                    SET value = EXCLUDED.value,
                        timestamp = EXCLUDED.timestamp,
                        updated_at = EXCLUDED.updated_at
                    WHERE sensor_states.timestamp < EXCLUDED.timestamp
                    """,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        _logger.LogDebug(
            "Wrote batch of {Count} sensor updates ({UniqueCount} unique sensors)",
            dataList.Count,
            latestBySensor.Count);
    }

    private static List<string> ValidatePayload(List<SensorData> data)
    {
        var errors = new List<string>();

        for (var i = 0; i < data.Count; i++)
        {
            var item = data[i];

            if (string.IsNullOrWhiteSpace(item.SensorId))
            {
                errors.Add($"Item {i}: SensorId is required");
            }

            if (double.IsNaN(item.Value) || double.IsInfinity(item.Value))
            {
                errors.Add($"Item {i}: Value must be a valid number");
            }

            if (item.Timestamp == default)
            {
                errors.Add($"Item {i}: Timestamp is required");
            }
        }

        return errors;
    }
}
