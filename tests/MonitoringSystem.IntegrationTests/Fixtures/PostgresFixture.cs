using Microsoft.EntityFrameworkCore;
using MonitoringSystem.Data;
using Testcontainers.PostgreSql;
using Xunit;

namespace MonitoringSystem.IntegrationTests.Fixtures;

/// <summary>
/// Fixture that manages a PostgreSQL container for integration testing.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    /// <summary>
    /// Gets the connection string for connecting to PostgreSQL.
    /// </summary>
    public string ConnectionString => _container?.GetConnectionString()
        ?? throw new InvalidOperationException("Container not started");

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16")
            .WithDatabase("monitoringdb")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await _container.StartAsync();

        // Create schema
        var options = new DbContextOptionsBuilder<MonitoringDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using var dbContext = new MonitoringDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// Creates a new DbContext instance for testing.
    /// </summary>
    public MonitoringDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MonitoringDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new MonitoringDbContext(options);
    }

    /// <summary>
    /// Creates a DbContextFactory for testing singleton services.
    /// </summary>
    public IDbContextFactory<MonitoringDbContext> CreateDbContextFactory()
    {
        return new TestDbContextFactory(ConnectionString);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<MonitoringDbContext>
    {
        private readonly string _connectionString;

        public TestDbContextFactory(string connectionString) => _connectionString = connectionString;

        public MonitoringDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<MonitoringDbContext>()
                .UseNpgsql(_connectionString)
                .Options;
            return new MonitoringDbContext(options);
        }
    }
}
