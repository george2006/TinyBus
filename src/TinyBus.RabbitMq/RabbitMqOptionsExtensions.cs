using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        Func<IServiceProvider, ITransport> createTransport = provider =>
        {
            var retryPolicy = provider.GetRequiredService<MessageRetryPolicy>();
            var logger = provider.GetRequiredService<ILogger<RabbitMqTransport>>();
            var transport = new RabbitMqTransport(
                connectionString,
                retryPolicy,
                logger);
            return transport;
        };
        services.AddSingleton(createTransport);

        return options;
    }
}
