namespace MonitoringSystem.Worker.Processing;

/// <summary>
/// Configuration options for batch processing.
/// </summary>
public sealed class ProcessingOptions
{
    public const string SectionName = "Processing";

    /// <summary>
    /// Gets or sets the maximum number of messages to batch before writing to database.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the maximum time to wait for a full batch before flushing.
    /// </summary>
    public TimeSpan BatchTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the maximum number of retries for transient failures.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the delay between retry attempts.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);
}
