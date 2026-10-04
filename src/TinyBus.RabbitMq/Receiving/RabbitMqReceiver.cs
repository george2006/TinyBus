using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TinyBus;

namespace TinyBus.RabbitMq.Receiving;

internal sealed class RabbitMqReceiver : IAsyncDisposable
{
    private const string BrokerDeliveryCountHeader = "x-delivery-count";

    private readonly IConnection connection;
    private readonly string queueName;
    private readonly RabbitMqDeadLetterPublisher deadLetterPublisher;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private Channel<ITransportDelivery>? deliveries;
    private IChannel? channel;
    private int maximumCapacity;

    internal RabbitMqReceiver(
        IConnection connection,
        ServiceAddress serviceAddress)
    {
        this.connection = connection;
        queueName = serviceAddress.QueueName;
        deadLetterPublisher = new RabbitMqDeadLetterPublisher(
            connection,
            serviceAddress);
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
        var attempt = ReadAttempt(arguments.BasicProperties);
        var properties = CopyProperties(arguments.BasicProperties);
        var body = arguments.Body.ToArray();
        var message = new RabbitMqIncomingCommand(
            arguments.DeliveryTag,
            attempt,
            properties,
            body);
        var delivery = new RabbitMqCommandDelivery(
            openedChannel,
            deadLetterPublisher,
            message);
        var deliveryChannel = deliveries!;

        await deliveryChannel.Writer.WriteAsync(
            delivery,
            arguments.CancellationToken);
    }

    private static BasicProperties CopyProperties(
        IReadOnlyBasicProperties source)
    {
        var properties = new BasicProperties(source);

        if (source.Headers is not null)
        {
            properties.Headers = new Dictionary<string, object?>(source.Headers);
        }

        return properties;
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

    private static int ReadAttempt(IReadOnlyBasicProperties properties)
    {
        var headers = properties.Headers;

        if (headers is null || !headers.TryGetValue(BrokerDeliveryCountHeader, out var value))
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

}
