using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TinyBus;

namespace TinyBus.RabbitMq.Receiving;

internal sealed class RabbitMqReceiver : IAsyncDisposable
{
    private const string CausationIdHeader = "tinybus-causation-id";
    private const string ContractVersionHeader = "tinybus-contract-version";
    private const string DeliveryCountHeader = "x-delivery-count";
    private const string HeadersHeader = "tinybus-headers";

    private readonly IConnection connection;
    private readonly string queueName;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private Channel<ITransportDelivery>? deliveries;
    private IChannel? channel;
    private int maximumCapacity;

    internal RabbitMqReceiver(IConnection connection, string queueName)
    {
        this.connection = connection;
        this.queueName = queueName;
    }

    internal async ValueTask<IReadOnlyList<ITransportDelivery>> ReceiveAsync(
        ReceiveCapacity capacity,
        CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(capacity.Maximum, cancellationToken);

        var deliveryChannel = deliveries!;
        var received = new List<ITransportDelivery>(capacity.Available);
        var firstDelivery = await deliveryChannel.Reader.ReadAsync(cancellationToken);
        received.Add(firstDelivery);

        while (received.Count < capacity.Available
            && deliveryChannel.Reader.TryRead(out var delivery))
        {
            received.Add(delivery);
        }

        return received;
    }

    public async ValueTask DisposeAsync()
    {
        var openedChannel = channel;
        channel = null;
        deliveries?.Writer.TryComplete();

        if (openedChannel is not null)
        {
            await openedChannel.DisposeAsync();
        }

        initializationLock.Dispose();
    }

    private async ValueTask EnsureStartedAsync(
        int requestedMaximum,
        CancellationToken cancellationToken)
    {
        if (channel is not null)
        {
            EnsureCapacityMatches(requestedMaximum);
            return;
        }

        await initializationLock.WaitAsync(cancellationToken);

        try
        {
            if (channel is not null)
            {
                EnsureCapacityMatches(requestedMaximum);
                return;
            }

            await StartAsync(requestedMaximum, cancellationToken);
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private async ValueTask StartAsync(
        int requestedMaximum,
        CancellationToken cancellationToken)
    {
        var prefetchCount = checked((ushort)requestedMaximum);
        var bufferOptions = new BoundedChannelOptions(requestedMaximum)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        };
        var deliveryChannel = Channel.CreateBounded<ITransportDelivery>(bufferOptions);
        var openedChannel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        try
        {
            await openedChannel.BasicQosAsync(
                prefetchSize: 0,
                prefetchCount,
                global: false,
                cancellationToken);
            var consumer = new AsyncEventingBasicConsumer(openedChannel);
            consumer.ReceivedAsync += OnDeliveryReceivedAsync;
            consumer.ShutdownAsync += OnConsumerShutdownAsync;

            maximumCapacity = requestedMaximum;
            deliveries = deliveryChannel;
            channel = openedChannel;

            await openedChannel.BasicConsumeAsync(
                queueName,
                autoAck: false,
                consumer,
                cancellationToken);
        }
        catch
        {
            channel = null;
            deliveries = null;
            deliveryChannel.Writer.TryComplete();
            await openedChannel.DisposeAsync();
            throw;
        }
    }

    private async Task OnDeliveryReceivedAsync(
        object sender,
        BasicDeliverEventArgs arguments)
    {
        var openedChannel = channel!;
        var envelope = ReadEnvelope(arguments);
        var attempt = ReadAttempt(arguments.BasicProperties);
        var delivery = new RabbitMqCommandDelivery(
            openedChannel,
            arguments.DeliveryTag,
            attempt,
            envelope);
        var deliveryChannel = deliveries!;

        await deliveryChannel.Writer.WriteAsync(
            delivery,
            arguments.CancellationToken);
    }

    private Task OnConsumerShutdownAsync(
        object sender,
        ShutdownEventArgs arguments)
    {
        deliveries?.Writer.TryComplete();

        return Task.CompletedTask;
    }

    private void EnsureCapacityMatches(int requestedMaximum)
    {
        if (requestedMaximum != maximumCapacity)
        {
            throw new InvalidOperationException(
                "RabbitMQ receive capacity cannot change after receiving has started.");
        }
    }

    private static MessageEnvelope ReadEnvelope(BasicDeliverEventArgs delivery)
    {
        var properties = delivery.BasicProperties;
        var messageId = ReadMessageId(properties);
        var contractName = ReadContractName(properties);
        var contractVersion = ReadContractVersion(properties);
        var payload = Encoding.UTF8.GetString(delivery.Body.Span);
        var causationId = ReadTextHeader(properties, CausationIdHeader);
        var headers = ReadHeaders(properties);
        var contract = new ContractIdentity(contractName, contractVersion);
        var envelope = new MessageEnvelope(
            messageId,
            contract,
            payload,
            properties.CorrelationId,
            causationId,
            headers);

        return envelope;
    }

    private static int ReadAttempt(IReadOnlyBasicProperties properties)
    {
        var headers = properties.Headers;

        if (headers is null || !headers.TryGetValue(DeliveryCountHeader, out var value))
        {
            return 1;
        }

        var deliveryCount = Convert.ToInt32(value, CultureInfo.InvariantCulture);

        if (deliveryCount < 0)
        {
            throw new InvalidOperationException(
                "The RabbitMQ delivery contains a negative delivery count.");
        }

        var attempt = checked(deliveryCount + 1);

        return attempt;
    }

    private static Guid ReadMessageId(IReadOnlyBasicProperties properties)
    {
        if (!Guid.TryParse(properties.MessageId, out var messageId))
        {
            throw new InvalidOperationException(
                "The RabbitMQ delivery does not contain a valid TinyBus message id.");
        }

        return messageId;
    }

    private static string ReadContractName(IReadOnlyBasicProperties properties)
    {
        if (string.IsNullOrWhiteSpace(properties.Type))
        {
            throw new InvalidOperationException(
                "The RabbitMQ delivery does not contain a TinyBus contract name.");
        }

        return properties.Type;
    }

    private static int ReadContractVersion(IReadOnlyBasicProperties properties)
    {
        var value = ReadRequiredHeader(properties, ContractVersionHeader);
        var version = Convert.ToInt32(value, CultureInfo.InvariantCulture);

        return version;
    }

    private static IReadOnlyDictionary<string, string>? ReadHeaders(
        IReadOnlyBasicProperties properties)
    {
        var serializedHeaders = ReadTextHeader(properties, HeadersHeader);

        if (serializedHeaders is null)
        {
            return null;
        }

        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(serializedHeaders);

        return headers;
    }

    private static string? ReadTextHeader(
        IReadOnlyBasicProperties properties,
        string name)
    {
        var headers = properties.Headers;

        if (headers is null || !headers.TryGetValue(name, out var value))
        {
            return null;
        }

        if (value is byte[] bytes)
        {
            var text = Encoding.UTF8.GetString(bytes);
            return text;
        }

        if (value is ReadOnlyMemory<byte> memory)
        {
            var text = Encoding.UTF8.GetString(memory.Span);
            return text;
        }

        if (value is string textValue)
        {
            return textValue;
        }

        var converted = Convert.ToString(value, CultureInfo.InvariantCulture);

        return converted;
    }

    private static object ReadRequiredHeader(
        IReadOnlyBasicProperties properties,
        string name)
    {
        var headers = properties.Headers;

        if (headers is null || !headers.TryGetValue(name, out var value) || value is null)
        {
            throw new InvalidOperationException(
                $"The RabbitMQ delivery does not contain the required '{name}' header.");
        }

        return value;
    }
}
