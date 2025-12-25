using FluentAssertions;
using MonitoringSystem.Core.Exceptions;
using Xunit;

namespace MonitoringSystem.UnitTests;

public class DataValidationExceptionTests
{
    [Fact]
    public void Constructor_Default_CreatesExceptionWithEmptyErrors()
    {
        // Act
        var exception = new DataValidationException();

        // Assert
        exception.ValidationErrors.Should().BeEmpty();
        exception.Message.Should().NotBeEmpty();
    }

    [Fact]
    public void Constructor_WithMessage_SetsMessage()
    {
        // Arrange
        const string message = "Validation failed";

        // Act
        var exception = new DataValidationException(message);

        // Assert
        exception.Message.Should().Be(message);
        exception.ValidationErrors.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithMessageAndInnerException_SetsBoth()
    {
        // Arrange
        const string message = "Validation failed";
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new DataValidationException(message, innerException);

        // Assert
        exception.Message.Should().Be(message);
        exception.InnerException.Should().Be(innerException);
    }

    [Fact]
    public void ValidationErrors_CanBeInitialized()
    {
        // Arrange
        var errors = new List<string> { "Error 1", "Error 2" };

        // Act
        var exception = new DataValidationException("Validation failed")
        {
            ValidationErrors = errors
        };

        // Assert
        exception.ValidationErrors.Should().BeEquivalentTo(errors);
    }
}
