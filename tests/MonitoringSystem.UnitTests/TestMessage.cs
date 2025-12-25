using MonitoringSystem.Core.Abstractions;
using MonitoringSystem.Core.Domain;

namespace MonitoringSystem.UnitTests;

/// <summary>
/// Test implementation of Message{SensorData} for unit testing.
/// </summary>
public sealed class TestMessage : Message<SensorData>
{
    private readonly SensorData _data;

    public TestMessage(SensorData data)
    {
        _data = data;
    }

    public override SensorData Data => _data;

    public bool WasAcked { get; private set; }
    public bool WasNacked { get; private set; }
    public bool WasRequeued { get; private set; }

    public override Task AckAsync()
    {
        WasAcked = true;
        return Task.CompletedTask;
    }

    public override Task NackAsync(bool requeue)
    {
        WasNacked = true;
        WasRequeued = requeue;
        return Task.CompletedTask;
    }
}
