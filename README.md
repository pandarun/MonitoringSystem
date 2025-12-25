# MonitoringSystem

A critical infrastructure monitoring system that processes sensor data from Kafka and stores the latest state per sensor in PostgreSQL.

## Architecture

```
Kafka Topic → KafkaMessageSource → BatchProcessor → SensorDataDestination → PostgreSQL
                                        ↓
                              SensorProcessor (BackgroundService)
```

### Key Components

| Component | Purpose |
|-----------|---------|
| `MonitoringSystem.Core` | Domain models, abstractions, exceptions |
| `MonitoringSystem.Data` | EF Core context, PostgreSQL repository |
| `MonitoringSystem.Kafka` | Kafka consumer, message serialization |
| `MonitoringSystem.Worker` | Background service, batch processing |
| `MonitoringSystem.Producer` | Demo producer for testing |
| `MonitoringSystem.AppHost` | .NET Aspire orchestration |

### Design Decisions

- **Batch Processing**: Messages are accumulated and flushed either when batch size (100) is reached or timeout (5s) expires
- **Upsert Semantics**: Database stores only the latest value per SensorId; older timestamps don't overwrite newer ones
- **Retry Logic**: Transient errors retry up to 3 times with backoff; validation errors skip the message
- **Thread-Safe Consumer**: Consumer operations are synchronized to ensure safe concurrent access

## Prerequisites

- .NET 10.0 SDK
- Docker (for PostgreSQL and Kafka in development/tests)

## Getting Started

### Run with .NET Aspire

```bash
cd src/MonitoringSystem.AppHost
dotnet run
```

### Run Worker Standalone

```bash
# Start Kafka (KRaft mode, no Zookeeper required)
docker run -d --name kafka -p 9092:9092 \
  -e KAFKA_NODE_ID=1 \
  -e KAFKA_PROCESS_ROLES=broker,controller \
  -e KAFKA_LISTENERS=PLAINTEXT://:9092,CONTROLLER://:9093 \
  -e KAFKA_ADVERTISED_LISTENERS=PLAINTEXT://localhost:9092 \
  -e KAFKA_CONTROLLER_LISTENER_NAMES=CONTROLLER \
  -e KAFKA_CONTROLLER_QUORUM_VOTERS=1@localhost:9093 \
  -e KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1 \
  -e CLUSTER_ID=MkU3OEVBNTcwNTJENDM2Qk \
  apache/kafka:latest

# Start PostgreSQL
docker run -d --name postgres -p 5432:5432 \
  -e POSTGRES_USER=postgres \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=monitoring \
  postgres:16

# Run worker
cd src/MonitoringSystem.Worker
dotnet run
```

> **Note**: Ensure ports 9092 and 5432 are available. For local development, using .NET Aspire (`dotnet run` in AppHost) is recommended as it manages all dependencies automatically.

### Configuration

`appsettings.json`:

```json
{
  "Kafka": {
    "Topic": "sensor-data",
    "GroupId": "sensor-processor-group",
    "ConsumeTimeoutMs": 1000
  },
  "Processing": {
    "BatchSize": 100,
    "BatchTimeout": "00:00:05",
    "MaxRetries": 3,
    "RetryDelay": "00:00:00.100"
  },
  "ConnectionStrings": {
    "postgresdb": "Host=localhost;Database=monitoring;Username=postgres;Password=postgres"
  }
}
```

## Testing

### Prerequisites

- **Docker**: Required for integration tests (Kafka and PostgreSQL run in containers)

### Run All Tests

```bash
# Ensure Docker daemon is running
docker info

# Run tests
dotnet test
```

### Test Infrastructure

Integration tests use [Testcontainers](https://dotnet.testcontainers.org/) to automatically spin up:
- **Kafka** (Confluent Platform) - message broker
- **PostgreSQL 16** - database

Containers are started automatically on test run and cleaned up after.

### Test Structure

- `MonitoringSystem.UnitTests` - Unit tests with mocked dependencies (no Docker required)
- `MonitoringSystem.IntegrationTests` - End-to-end tests with real Kafka/PostgreSQL (Docker required)

## Project Structure

```
MonitoringSystem/
├── src/
│   ├── MonitoringSystem.Core/          # Domain models, interfaces
│   ├── MonitoringSystem.Data/          # EF Core, PostgreSQL
│   ├── MonitoringSystem.Kafka/         # Kafka integration
│   ├── MonitoringSystem.Worker/        # Background service
│   ├── MonitoringSystem.Producer/      # Demo producer
│   ├── MonitoringSystem.AppHost/       # Aspire host
│   └── MonitoringSystem.ServiceDefaults/ # OpenTelemetry, health checks
└── tests/
    ├── MonitoringSystem.UnitTests/
    └── MonitoringSystem.IntegrationTests/
```

## License

This project is a test assignment submission.
