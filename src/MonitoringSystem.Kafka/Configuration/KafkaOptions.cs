namespace MonitoringSystem.Kafka.Configuration;

/// <summary>
/// Configuration options for Kafka consumer.
/// </summary>
public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    /// <summary>
    /// Gets or sets the Kafka topic to consume from.
    /// </summary>
    public string Topic { get; set; } = "sensor-data";

    /// <summary>
    /// Gets or sets the consumer group ID.
    /// </summary>
    public string GroupId { get; set; } = "sensor-processor-group";

    /// <summary>
    /// Gets or sets the consume timeout in milliseconds.
    /// </summary>
    public int ConsumeTimeoutMs { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the retry topic for failed messages.
    /// </summary>
    public string? RetryTopic { get; set; }

    /// <summary>
    /// Gets or sets the dead letter queue topic.
    /// </summary>
    public string? DeadLetterTopic { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of retries for failed messages.
    /// </summary>
    public int MaxRetries { get; set; } = 3;
}
