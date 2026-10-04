using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TinyBus;

namespace TinyBus.PostgreSql;

public static class PostgreSqlOptionsExtensions
{
    /// <summary>
    /// Uses PostgreSQL as the transport for this TinyBus runtime.
    /// </summary>
    public static TinyBusOptions UsePostgreSql(
        this TinyBusOptions options,
        string connectionString)
    {
        return UsePostgreSql(options, connectionString, configure: null);
    }

    /// <summary>
    /// Uses PostgreSQL as the transport for this TinyBus runtime.
    /// </summary>
    public static TinyBusOptions UsePostgreSql(
        this TinyBusOptions options,
        string connectionString,
        Action<PostgreSqlOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var postgreSqlOptions = new PostgreSqlOptions();
        configure?.Invoke(postgreSqlOptions);
        var commandLeaseDuration = postgreSqlOptions.CommandLeaseDuration;
        var services = options.Services;
        Func<IServiceProvider, ITransport> createTransport = provider =>
        {
            var logger = provider.GetRequiredService<ILogger<PostgreSqlTransport>>();
            var transport = new PostgreSqlTransport(
                connectionString,
                commandLeaseDuration,
                logger);

            return transport;
        };
        services.AddSingleton(createTransport);

        return options;
    }
}
