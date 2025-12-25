namespace MonitoringSystem.Core.Abstractions;

/// <summary>
/// Represents a message from a message source with acknowledgment capabilities.
/// </summary>
/// <typeparam name="T">The type of data contained in the message.</typeparam>
public abstract class Message<T>
{
    /// <summary>
    /// Gets the data payload of the message.
    /// </summary>
    public abstract T Data { get; }

    /// <summary>
    /// Acknowledges successful processing of the message.
    /// </summary>
    public abstract Task AckAsync();

    /// <summary>
    /// Negatively acknowledges the message, indicating processing failure.
    /// </summary>
    /// <param name="requeue">If true, the message should be requeued for retry.</param>
    public abstract Task NackAsync(bool requeue);
}
