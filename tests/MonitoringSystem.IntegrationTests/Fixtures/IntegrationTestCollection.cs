using Xunit;

namespace MonitoringSystem.IntegrationTests.Fixtures;

/// <summary>
/// Collection definition for integration tests that share Kafka and PostgreSQL fixtures.
/// </summary>
[CollectionDefinition("Integration")]
public class IntegrationTestCollection
    : ICollectionFixture<KafkaFixture>, ICollectionFixture<PostgresFixture>
{
    // This class has no code - it's just a marker for xUnit collection fixtures
}
