using System;
using Microsoft.Extensions.DependencyInjection;
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
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var transport = new PostgreSqlTransport(connectionString);
        var services = options.Services;
        services.AddSingleton<ITransport>(transport);

        return options;
    }
}
