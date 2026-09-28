using System;
using System.Text;
using TinyBus;

namespace TinyBus.RabbitMq;

internal readonly record struct ServiceAddress(
    string QueueName,
    string DeadLetterQueueName)
{
    internal const string DeadLetterExchangeName = "tinybus.dead-letters";
    private const string DeadLetterSuffix = ".dead-letter";
    private const int MaximumQueueNameLength = 255;
    private const string QueuePrefix = "tinybus.";

    public static ServiceAddress From(ServiceIdentity service)
    {
        if (string.IsNullOrWhiteSpace(service.Value))
        {
            throw new ArgumentException("A RabbitMQ service address requires a service identity.", nameof(service));
        }

        var queueName = QueuePrefix + service.Value;
        var deadLetterQueueName = queueName + DeadLetterSuffix;
        ValidateServiceQueueLength(queueName);
        ValidateDeadLetterQueueLength(deadLetterQueueName);

        var address = new ServiceAddress(queueName, deadLetterQueueName);
        return address;
    }

    private static void ValidateServiceQueueLength(string queueName)
    {
        ValidateLength(queueName, "service");
    }

    private static void ValidateDeadLetterQueueLength(string queueName)
    {
        ValidateLength(queueName, "service dead-letter");
    }

    private static void ValidateLength(string queueName, string purpose)
    {
        var length = Encoding.UTF8.GetByteCount(queueName);
        if (length > MaximumQueueNameLength)
        {
            throw new ArgumentException(
                $"A RabbitMQ {purpose} queue name cannot exceed 255 UTF-8 bytes.",
                nameof(queueName));
        }
    }
}
