using System.Text.Json;
using Confluent.Kafka;
using MonitoringSystem.Core.Domain;

namespace MonitoringSystem.Kafka.Serialization;

/// <summary>
/// JSON serializer and deserializer for SensorData.
/// </summary>
public sealed class SensorDataSerializer : ISerializer<SensorData>, IDeserializer<SensorData>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public byte[] Serialize(SensorData data, SerializationContext context)
    {
        return JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions);
    }

    public SensorData Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
    {
        if (isNull || data.IsEmpty)
        {
            throw new ArgumentException("Cannot deserialize null or empty data");
        }

        return JsonSerializer.Deserialize<SensorData>(data, JsonOptions)
               ?? throw new InvalidOperationException("Deserialization returned null");
    }
}
