using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using TinyBus;
using TinyBus.RabbitMq.Receiving;

namespace TinyBus.RabbitMq;

internal sealed class RabbitMqTransport : ITransport, IAsyncDisposable
{
    private const string CausationIdHeader = "tinybus-causation-id";
    private const string ContractVersionHeader = "tinybus-contract-version";
    private const string DeadLetterExchangeArgument = "x-dead-letter-exchange";
    private const string DeadLetterRoutingKeyArgument = "x-dead-letter-routing-key";
    private const string DeadLetterStrategyArgument = "x-dead-letter-strategy";
    private const string DelayedRetryMaximumArgument = "x-delayed-retry-max";
    private const string DelayedRetryMinimumArgument = "x-delayed-retry-min";
    private const string DelayedRetryTypeArgument = "x-delayed-retry-type";
    private const string DeliveryLimitArgument = "x-delivery-limit";
    private const string HeadersHeader = "tinybus-headers";
    private const string OverflowArgument = "x-overflow";
    private const string QueueTypeArgument = "x-queue-type";

    private readonly Uri connectionUri;
    private readonly MessageRetryPolicy retryPolicy;
    private IConnection? connection;
    private RabbitMqReceiver? receiver;

    public RabbitMqTransport(
        string connectionString,
        MessageRetryPolicy retryPolicy)
    {
        ArgumentNullException.ThrowIfNull(retryPolicy);

        connectionUri = ReadConnectionUri(connectionString);
        this.retryPolicy = retryPolicy;
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
                retryPolicy,
                cancellationToken).ConfigureAwait(false);

            receiver = new RabbitMqReceiver(
                openedConnection,
                serviceAddress);
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
        var activeReceiver = receiver;
        connection = null;
        receiver = null;

        if (activeReceiver is not null)
        {
            await activeReceiver.DisposeAsync().ConfigureAwait(false);
        }

        if (openedConnection is null)
        {
            return;
        }

        await openedConnection.DisposeAsync().ConfigureAwait(false);
    }

    public ValueTask<IReadOnlyList<ITransportDelivery>> ReceiveAsync(
        ReceiveCapacity capacity,
        CancellationToken cancellationToken = default)
    {
        var activeReceiver = GetReceiver();
        var receiving = activeReceiver.ReceiveAsync(capacity, cancellationToken);

        return receiving;
    }

    public async ValueTask SendAsync(
        MessageEnvelope message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var openedConnection = GetConnection();
        var address = CommandAddress.From(message.Contract);
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);
        await using var channel = await openedConnection
            .CreateChannelAsync(channelOptions, cancellationToken)
            .ConfigureAwait(false);
        var properties = CreateProperties(message);
        var body = Encoding.UTF8.GetBytes(message.Payload);

        await channel.BasicPublishAsync(
            address.Exchange,
            address.RoutingKey,
            mandatory: true,
            properties,
            body,
            cancellationToken).ConfigureAwait(false);
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
        MessageRetryPolicy retryPolicy,
        CancellationToken cancellationToken)
    {
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: false,
            publisherConfirmationTrackingEnabled: false);
        await using var channel = await connection
            .CreateChannelAsync(channelOptions, cancellationToken)
            .ConfigureAwait(false);

        await DeclareCommandExchangeAsync(channel, cancellationToken).ConfigureAwait(false);
        await DeclareDeadLetterTopologyAsync(
            channel,
            serviceAddress,
            cancellationToken).ConfigureAwait(false);
        await DeclareServiceQueueAsync(
            channel,
            serviceAddress,
            retryPolicy,
            cancellationToken).ConfigureAwait(false);
        await BindCommandsAsync(
            channel,
            serviceAddress,
            commandAddresses,
            cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask DeclareDeadLetterTopologyAsync(
        IChannel channel,
        ServiceAddress serviceAddress,
        CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            ServiceAddress.DeadLetterExchangeName,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null,
            noWait: false,
            cancellationToken).ConfigureAwait(false);

        var arguments = new Dictionary<string, object?>
        {
            [QueueTypeArgument] = "quorum"
        };
        await channel.QueueDeclareAsync(
            serviceAddress.DeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments,
            noWait: false,
            cancellationToken).ConfigureAwait(false);

        await channel.QueueBindAsync(
            serviceAddress.DeadLetterQueueName,
            ServiceAddress.DeadLetterExchangeName,
            serviceAddress.QueueName,
            arguments: null,
            noWait: false,
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
        MessageRetryPolicy retryPolicy,
        CancellationToken cancellationToken)
    {
        var minimumDelay = ReadMilliseconds(retryPolicy.MinimumDelay);
        var maximumDelay = ReadMilliseconds(retryPolicy.MaximumDelay);
        var arguments = new Dictionary<string, object?>
        {
            [QueueTypeArgument] = "quorum",
            [DeadLetterExchangeArgument] = ServiceAddress.DeadLetterExchangeName,
            [DeadLetterRoutingKeyArgument] = serviceAddress.QueueName,
            [DeadLetterStrategyArgument] = "at-least-once",
            [OverflowArgument] = "reject-publish",
            [DelayedRetryTypeArgument] = "failed",
            [DelayedRetryMinimumArgument] = minimumDelay,
            [DelayedRetryMaximumArgument] = maximumDelay,
            [DeliveryLimitArgument] = retryPolicy.MaximumAttempts
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

    private static long ReadMilliseconds(TimeSpan delay)
    {
        var roundedMilliseconds = Math.Ceiling(delay.TotalMilliseconds);
        var milliseconds = checked((long)roundedMilliseconds);

        return milliseconds;
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

    private IConnection GetConnection()
    {
        if (connection is null)
        {
            throw new InvalidOperationException("The RabbitMQ transport has not been initialized.");
        }

        return connection;
    }

    private RabbitMqReceiver GetReceiver()
    {
        if (receiver is null)
        {
            throw new InvalidOperationException("The RabbitMQ transport has not been initialized.");
        }

        return receiver;
    }

    private static BasicProperties CreateProperties(MessageEnvelope message)
    {
        var headers = CreateHeaders(message);
        var messageId = message.MessageId.ToString("D");
        var properties = new BasicProperties
        {
            ContentEncoding = "utf-8",
            ContentType = "application/json",
            CorrelationId = message.CorrelationId,
            Headers = headers,
            MessageId = messageId,
            Persistent = true,
            Type = message.Contract.Name
        };

        return properties;
    }

    private static IDictionary<string, object?> CreateHeaders(MessageEnvelope message)
    {
        var headers = new Dictionary<string, object?>
        {
            [ContractVersionHeader] = message.Contract.Version
        };

        if (message.CausationId is not null)
        {
            headers[CausationIdHeader] = message.CausationId;
        }

        if (message.Headers is not null)
        {
            var serializedHeaders = JsonSerializer.Serialize(message.Headers);
            headers[HeadersHeader] = serializedHeaders;
        }

        return headers;
    }
}
