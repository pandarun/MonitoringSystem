using Microsoft.Extensions.DependencyInjection;
using MonitoringSystem.Core.Abstractions;
using MonitoringSystem.Data.Repositories;

namespace MonitoringSystem.Data;

/// <summary>
/// Extension methods for registering Data services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds data destination services to the service collection.
    /// Registers as singleton for use with background services.
    /// </summary>
    public static IServiceCollection AddSensorDataDestination(this IServiceCollection services)
    {
        services.AddSingleton<IDataDestination, SensorDataDestination>();
        return services;
    }
}
