using System;
using Microsoft.Extensions.DependencyInjection;
using TinyBus;

namespace TinyBus.RabbitMq;

public static class RabbitMqOptionsExtensions
{
    /// <summary>
    /// Uses RabbitMQ as the transport for this TinyBus runtime.
    /// </summary>
    public static TinyBusOptions UseRabbitMq(
        this TinyBusOptions options,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var services = options.Services;
        Func<IServiceProvider, ITransport> createTransport = _ =>
        {
            var transport = new RabbitMqTransport(connectionString);
            return transport;
        };
        services.AddSingleton(createTransport);

        return options;
    }
}
