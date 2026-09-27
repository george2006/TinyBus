using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using TinyBus;

namespace TinyBus.RabbitMq;

internal sealed class RabbitMqTransport : ITransport, IAsyncDisposable
{
    private const string QueueTypeArgument = "x-queue-type";

    private readonly Uri connectionUri;
    private IConnection? connection;

    public RabbitMqTransport(string connectionString)
    {
        connectionUri = ReadConnectionUri(connectionString);
    }

    public async ValueTask InitializeAsync(
        ServiceTopology topology,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ThrowIfInitialized();

        var serviceAddress = ServiceAddress.From(topology.Service);
        var commandAddresses = ReadCommandAddresses(topology);
        var openedConnection = await OpenConnectionAsync(serviceAddress, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var journal = new RabbitMqTopologyJournal(openedConnection);
            await journal.ReconcileAsync(topology, cancellationToken).ConfigureAwait(false);

            await DeclarePhysicalTopologyAsync(
                openedConnection,
                serviceAddress,
                commandAddresses,
                cancellationToken).ConfigureAwait(false);

            connection = openedConnection;
        }
        catch
        {
            await openedConnection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var openedConnection = connection;
        connection = null;

        if (openedConnection is null)
        {
            return;
        }

        await openedConnection.DisposeAsync().ConfigureAwait(false);
    }

    private async ValueTask<IConnection> OpenConnectionAsync(
        ServiceAddress serviceAddress,
        CancellationToken cancellationToken)
    {
        var connectionFactory = new ConnectionFactory
        {
            Uri = connectionUri
        };
        var connectionName = serviceAddress.QueueName;
        var openedConnection = await connectionFactory
            .CreateConnectionAsync(connectionName, cancellationToken)
            .ConfigureAwait(false);

        return openedConnection;
    }

    private static async ValueTask DeclarePhysicalTopologyAsync(
        IConnection connection,
        ServiceAddress serviceAddress,
        IReadOnlyCollection<CommandAddress> commandAddresses,
        CancellationToken cancellationToken)
    {
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: false,
            publisherConfirmationTrackingEnabled: false);
        await using var channel = await connection
            .CreateChannelAsync(channelOptions, cancellationToken)
            .ConfigureAwait(false);

        await DeclareCommandExchangeAsync(channel, cancellationToken).ConfigureAwait(false);
        await DeclareServiceQueueAsync(channel, serviceAddress, cancellationToken).ConfigureAwait(false);
        await BindCommandsAsync(
            channel,
            serviceAddress,
            commandAddresses,
            cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask DeclareCommandExchangeAsync(
        IChannel channel,
        CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            CommandAddress.ExchangeName,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null,
            noWait: false,
            cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask DeclareServiceQueueAsync(
        IChannel channel,
        ServiceAddress serviceAddress,
        CancellationToken cancellationToken)
    {
        var arguments = new Dictionary<string, object?>
        {
            [QueueTypeArgument] = "quorum"
        };

        await channel.QueueDeclareAsync(
            serviceAddress.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments,
            noWait: false,
            cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask BindCommandsAsync(
        IChannel channel,
        ServiceAddress serviceAddress,
        IReadOnlyCollection<CommandAddress> commandAddresses,
        CancellationToken cancellationToken)
    {
        foreach (var commandAddress in commandAddresses)
        {
            await channel.QueueBindAsync(
                serviceAddress.QueueName,
                commandAddress.Exchange,
                commandAddress.RoutingKey,
                arguments: null,
                noWait: false,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static CommandAddress[] ReadCommandAddresses(ServiceTopology topology)
    {
        var uniqueAddresses = new HashSet<CommandAddress>();
        var addresses = new List<CommandAddress>();

        foreach (var message in topology.Messages)
        {
            if (message.Kind != MessageKind.Command)
            {
                continue;
            }

            var address = CommandAddress.From(message.Contract);
            if (uniqueAddresses.Add(address))
            {
                addresses.Add(address);
            }
        }

        return addresses.ToArray();
    }

    private static Uri ReadConnectionUri(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var isAbsolute = Uri.TryCreate(connectionString, UriKind.Absolute, out var connectionUri);
        if (!isAbsolute || connectionUri is null)
        {
            throw new ArgumentException(
                "A RabbitMQ connection string must be an absolute amqp or amqps URI.",
                nameof(connectionString));
        }

        var hasSupportedScheme = connectionUri.Scheme is "amqp" or "amqps";
        if (!hasSupportedScheme)
        {
            throw new ArgumentException(
                "A RabbitMQ connection string must be an absolute amqp or amqps URI.",
                nameof(connectionString));
        }

        return connectionUri;
    }

    private void ThrowIfInitialized()
    {
        if (connection is not null)
        {
            throw new InvalidOperationException("The RabbitMQ transport is already initialized.");
        }
    }
}
