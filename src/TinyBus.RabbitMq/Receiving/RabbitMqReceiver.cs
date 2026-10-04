using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using TinyBus;

namespace TinyBus.RabbitMq.Receiving;

internal sealed class RabbitMqReceiver : IAsyncDisposable
{
    private const string BrokerDeliveryCountHeader = "x-delivery-count";

    private readonly IConnection connection;
    private readonly string queueName;
    private readonly RabbitMqDeadLetterPublisher deadLetterPublisher;
    private readonly ILogger logger;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private Channel<ITransportDelivery>? deliveries;
    private AsyncEventingBasicConsumer? consumer;
    private IChannel? channel;
    private int maximumCapacity;

    internal RabbitMqReceiver(
        IConnection connection,
        ServiceAddress serviceAddress,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        this.connection = connection;
        queueName = serviceAddress.QueueName;
        deadLetterPublisher = new RabbitMqDeadLetterPublisher(
            connection,
            serviceAddress);
        this.logger = logger;
    }

    internal async ValueTask<IReadOnlyList<ITransportDelivery>> ReceiveAsync(
        ReceiveCapacity capacity,
        CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(capacity.Maximum, cancellationToken);

        while (true)
        {
            var deliveryChannel = deliveries!;

            try
            {
                var received = await ReadDeliveriesAsync(
                    deliveryChannel,
                    capacity.Available,
                    cancellationToken);

                return received;
            }
            catch (ChannelClosedException) when (ReceiveBufferWasReplaced(deliveryChannel))
            {
                continue;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        var openedChannel = channel;
        channel = null;
        consumer = null;
        deliveries?.Writer.TryComplete();

        if (openedChannel is not null)
        {
            await openedChannel.CloseAsync().ConfigureAwait(false);
            await openedChannel.DisposeAsync().ConfigureAwait(false);
        }

        initializationLock.Dispose();
    }

    internal async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        var activeChannel = channel;
        var activeConsumer = consumer;

        if (activeChannel is null || activeConsumer is null)
        {
            return;
        }

        var consumerTags = new List<string>(activeConsumer.ConsumerTags);

        foreach (var consumerTag in consumerTags)
        {
            await activeChannel.BasicCancelAsync(
                consumerTag,
                noWait: false,
                cancellationToken).ConfigureAwait(false);
        }
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
        var deliveryChannel = CreateDeliveryChannel(requestedMaximum);
        var openedChannel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        try
        {
            await openedChannel.BasicQosAsync(
                prefetchSize: 0,
                prefetchCount,
                global: false,
                cancellationToken);
            var startedConsumer = new AsyncEventingBasicConsumer(openedChannel);
            startedConsumer.ReceivedAsync += OnDeliveryReceivedAsync;
            startedConsumer.ShutdownAsync += OnConsumerShutdownAsync;

            maximumCapacity = requestedMaximum;
            deliveries = deliveryChannel;
            consumer = startedConsumer;
            channel = openedChannel;

            await openedChannel.BasicConsumeAsync(
                queueName,
                autoAck: false,
                startedConsumer,
                cancellationToken);
        }
        catch
        {
            channel = null;
            consumer = null;
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
        var connectionRecoveryWillRestoreConsumer = !connection.IsOpen;

        if (connectionRecoveryWillRestoreConsumer)
        {
            ReplaceReceiveBuffer();
            logger.LogWarning(
                arguments.Exception,
                "TinyBus RabbitMQ receiving was interrupted and will recover.");

            return Task.CompletedTask;
        }

        var exception = new OperationInterruptedException(arguments);
        deliveries?.Writer.TryComplete(exception);

        return Task.CompletedTask;
    }

    private void ReplaceReceiveBuffer()
    {
        var replacement = CreateDeliveryChannel(maximumCapacity);
        var interrupted = Interlocked.Exchange(ref deliveries, replacement);

        if (interrupted is null)
        {
            return;
        }

        while (interrupted.Reader.TryRead(out _))
        {
        }

        interrupted.Writer.TryComplete();
    }

    private bool ReceiveBufferWasReplaced(Channel<ITransportDelivery> observed)
    {
        var current = Volatile.Read(ref deliveries);

        return !ReferenceEquals(observed, current);
    }

    private static async ValueTask<IReadOnlyList<ITransportDelivery>> ReadDeliveriesAsync(
        Channel<ITransportDelivery> deliveryChannel,
        int availableCapacity,
        CancellationToken cancellationToken)
    {
        var received = new List<ITransportDelivery>(availableCapacity);
        var firstDelivery = await deliveryChannel.Reader.ReadAsync(cancellationToken);
        received.Add(firstDelivery);

        while (received.Count < availableCapacity
            && deliveryChannel.Reader.TryRead(out var delivery))
        {
            received.Add(delivery);
        }

        return received;
    }

    private static Channel<ITransportDelivery> CreateDeliveryChannel(int maximumCapacity)
    {
        var bufferOptions = new BoundedChannelOptions(maximumCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        };
        var deliveryChannel = Channel.CreateBounded<ITransportDelivery>(bufferOptions);

        return deliveryChannel;
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
