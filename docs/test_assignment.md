Вот тестовое, некоторые справляются достаточно быстро :
Вы проектируете компонент в системе мониторинга критической инфраструктуры.
Данные с датчиков поступают в очередь (Kafka-style), скорость поступления сообщений - неравномерная.
Ваша задача — реализовать консьюмер SensorProcessor, который перекладывает данные из очереди в базу.
В БД для каждого SensorId хранится только одно последнее состояние (SensorData).

public record SensorData(string SensorId, double Value, DateTime Timestamp);

public class DataValidationException : Exception { };

public class Message<T>
{
public T Data { get; }

public Task AckAsync();

public Task NackAsync(bool requeue);
}

public interface IMessageSource
{
/// <remarks>
/// Если в данный момент сообщений нет, метод не завершает выполнение сразу,
/// а асинхронно ожидает появления нового сообщения.
/// Возвращает null, если источник завершил работу и новых сообщений больше не ожидается.
/// </remarks>
Task<Message<SensorData>> ReceiveAsync(CancellationToken cancellationToken);
}

public interface IDataDestination
{
/// <exception cref="DataValidationException" />
/// <exception cref="DbUpdateException" />
Task WriteBatchAsync(IEnumerable<SensorData> payload, CancellationToken cancellationToken);
}