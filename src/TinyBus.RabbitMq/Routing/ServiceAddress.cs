using System;
using System.Text;
using TinyBus;

namespace TinyBus.RabbitMq;

internal readonly record struct ServiceAddress(string QueueName)
{
    private const int MaximumQueueNameLength = 255;
    private const string QueuePrefix = "tinybus.";

    public static ServiceAddress From(ServiceIdentity service)
    {
        if (string.IsNullOrWhiteSpace(service.Value))
        {
            throw new ArgumentException("A RabbitMQ service address requires a service identity.", nameof(service));
        }

        var queueName = QueuePrefix + service.Value;
        ValidateLength(queueName);

        var address = new ServiceAddress(queueName);
        return address;
    }

    private static void ValidateLength(string queueName)
    {
        var length = Encoding.UTF8.GetByteCount(queueName);
        if (length > MaximumQueueNameLength)
        {
            throw new ArgumentException(
                "A RabbitMQ service queue name cannot exceed 255 UTF-8 bytes.",
                nameof(queueName));
        }
    }
}
