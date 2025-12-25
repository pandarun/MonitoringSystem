using MonitoringSystem.Core.Domain;

namespace MonitoringSystem.Core.Abstractions;

/// <summary>
/// Interface for receiving sensor data messages from a message queue.
/// </summary>
public interface IMessageSource
{
    /// <summary>
    /// Receives the next available message from the source.
    /// </summary>
    /// <remarks>
    /// If no messages are currently available, the method asynchronously waits
    /// for a new message to arrive. Returns null if the source has completed
    /// and no more messages are expected.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>The next message, or null if the source has completed.</returns>
    Task<Message<SensorData>?> ReceiveAsync(CancellationToken cancellationToken);
}
