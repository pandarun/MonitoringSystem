using Microsoft.Extensions.DependencyInjection;
using MonitoringSystem.Core.Abstractions;
using MonitoringSystem.Kafka.Configuration;

namespace MonitoringSystem.Kafka;

/// <summary>
/// Extension methods for registering Kafka services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Kafka message source services to the service collection.
    /// Registers as singleton for use with background services.
    /// </summary>
    public static IServiceCollection AddKafkaMessageSource(
        this IServiceCollection services,
        Action<KafkaOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        services.AddSingleton<IMessageSource, KafkaMessageSource>();

        return services;
    }
}
