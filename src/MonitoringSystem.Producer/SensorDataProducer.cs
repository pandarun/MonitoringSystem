using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MonitoringSystem.Core.Domain;

namespace MonitoringSystem.Producer;

/// <summary>
/// Demo producer that generates simulated sensor data.
/// </summary>
public sealed class SensorDataProducer : BackgroundService
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<SensorDataProducer> _logger;
    private readonly string _topic;
    private readonly string[] _sensorIds;
    private readonly Random _random = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SensorDataProducer(
        IProducer<string, string> producer,
        IConfiguration configuration,
        ILogger<SensorDataProducer> logger)
    {
        _producer = producer;
        _logger = logger;
        _topic = configuration.GetValue<string>("Kafka:Topic") ?? "sensor-data";
        _sensorIds = Enumerable.Range(1, 10)
            .Select(i => $"sensor-{i:D3}")
            .ToArray();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "SensorDataProducer starting, producing to topic {Topic}",
            _topic);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Generate random sensor data
                var sensorId = _sensorIds[_random.Next(_sensorIds.Length)];
                var value = Math.Round(_random.NextDouble() * 100, 2);
                var timestamp = DateTime.UtcNow;

                var sensorData = new SensorData(sensorId, value, timestamp);

                var message = new Message<string, string>
                {
                    Key = sensorId,
                    Value = JsonSerializer.Serialize(sensorData, JsonOptions)
                };

                var result = await _producer.ProduceAsync(_topic, message, stoppingToken);

                _logger.LogDebug(
                    "Produced: {SensorId} = {Value} to {TopicPartitionOffset}",
                    sensorId,
                    value,
                    result.TopicPartitionOffset);

                // Simulate uneven message arrival rate
                var delay = _random.Next(10, 200);
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error producing message");
                await Task.Delay(1000, stoppingToken);
            }
        }

        _producer.Flush(TimeSpan.FromSeconds(10));
        _logger.LogInformation("SensorDataProducer stopped");
    }
}
