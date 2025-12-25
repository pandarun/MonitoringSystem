namespace MonitoringSystem.Core.Domain;

/// <summary>
/// Represents sensor data received from monitoring infrastructure.
/// </summary>
public sealed record SensorData(
    string SensorId,
    double Value,
    DateTime Timestamp
)
{
    /// <summary>
    /// Validates the sensor data.
    /// </summary>
    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(SensorId) &&
        !double.IsNaN(Value) &&
        !double.IsInfinity(Value) &&
        Timestamp != default;
}
