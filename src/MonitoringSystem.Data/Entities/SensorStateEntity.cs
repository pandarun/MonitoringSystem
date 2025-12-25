using System.ComponentModel.DataAnnotations;

namespace MonitoringSystem.Data.Entities;

/// <summary>
/// Database entity representing the latest state of a sensor.
/// </summary>
public class SensorStateEntity
{
    /// <summary>
    /// Gets or sets the unique sensor identifier.
    /// </summary>
    [Key]
    [MaxLength(256)]
    public required string SensorId { get; set; }

    /// <summary>
    /// Gets or sets the latest sensor value.
    /// </summary>
    public double Value { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the sensor reading.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Gets or sets when this record was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
