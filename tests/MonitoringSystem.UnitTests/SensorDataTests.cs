using FluentAssertions;
using MonitoringSystem.Core.Domain;
using Xunit;

namespace MonitoringSystem.UnitTests;

public class SensorDataTests
{
    [Fact]
    public void IsValid_WithValidData_ReturnsTrue()
    {
        // Arrange
        var sensorData = new SensorData("sensor-001", 42.5, DateTime.UtcNow);

        // Act
        var isValid = sensorData.IsValid();

        // Assert
        isValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void IsValid_WithInvalidSensorId_ReturnsFalse(string? sensorId)
    {
        // Arrange
        var sensorData = new SensorData(sensorId!, 42.5, DateTime.UtcNow);

        // Act
        var isValid = sensorData.IsValid();

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public void IsValid_WithNaNValue_ReturnsFalse()
    {
        // Arrange
        var sensorData = new SensorData("sensor-001", double.NaN, DateTime.UtcNow);

        // Act
        var isValid = sensorData.IsValid();

        // Assert
        isValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void IsValid_WithInfinityValue_ReturnsFalse(double value)
    {
        // Arrange
        var sensorData = new SensorData("sensor-001", value, DateTime.UtcNow);

        // Act
        var isValid = sensorData.IsValid();

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public void IsValid_WithDefaultTimestamp_ReturnsFalse()
    {
        // Arrange
        var sensorData = new SensorData("sensor-001", 42.5, default);

        // Act
        var isValid = sensorData.IsValid();

        // Assert
        isValid.Should().BeFalse();
    }
}
