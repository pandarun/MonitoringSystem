namespace MonitoringSystem.Core.Exceptions;

/// <summary>
/// Exception thrown when sensor data validation fails.
/// </summary>
public class DataValidationException : Exception
{
    public DataValidationException()
    {
    }

    public DataValidationException(string message)
        : base(message)
    {
    }

    public DataValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets the list of validation errors that occurred.
    /// </summary>
    public IReadOnlyList<string> ValidationErrors { get; init; } = [];
}
