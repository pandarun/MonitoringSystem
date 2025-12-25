using MonitoringSystem.Core.Domain;
using MonitoringSystem.Core.Exceptions;

namespace MonitoringSystem.Core.Abstractions;

/// <summary>
/// Interface for writing sensor data to a persistent store.
/// </summary>
public interface IDataDestination
{
    /// <summary>
    /// Writes a batch of sensor data to the destination.
    /// Only the latest value per SensorId should be stored.
    /// </summary>
    /// <param name="payload">The sensor data to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="DataValidationException">Thrown when data validation fails.</exception>
    /// <exception cref="Microsoft.EntityFrameworkCore.DbUpdateException">Thrown when database update fails.</exception>
    Task WriteBatchAsync(IEnumerable<SensorData> payload, CancellationToken cancellationToken);
}
