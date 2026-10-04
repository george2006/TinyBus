using System;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using TinyBus;

namespace TinyBus.RabbitMq.Receiving;

internal sealed class RabbitMqCommandDelivery : ITransportDelivery
{
    private readonly IChannel channel;
    private readonly RabbitMqDeadLetterPublisher deadLetterPublisher;
    private readonly RabbitMqIncomingCommand message;
    private int settlementState;

    internal RabbitMqCommandDelivery(
        IChannel channel,
        RabbitMqDeadLetterPublisher deadLetterPublisher,
        RabbitMqIncomingCommand message)
    {
        this.channel = channel;
        this.deadLetterPublisher = deadLetterPublisher;
        this.message = message;
    }

    public Guid? MessageId => message.Envelope.MessageId;

    public MessageEnvelope ReadEnvelope()
    {
        return message.Envelope;
    }

    public int Attempt => message.Attempt;

    public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        BeginSettlement();

        try
        {
            await channel.BasicAckAsync(
                message.DeliveryTag,
                multiple: false,
                cancellationToken);
            CompleteSettlement();
        }
        catch
        {
            ResetSettlement();
            throw;
        }
    }

    public async ValueTask ScheduleRetryAsync(
        Exception error,
        TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        BeginSettlement();

        try
        {
            await channel.BasicRejectAsync(
                message.DeliveryTag,
                requeue: true,
                cancellationToken);
            CompleteSettlement();
        }
        catch
        {
            ResetSettlement();
            throw;
        }
    }

    public async ValueTask DeadLetterAsync(
        Exception error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(error);
        BeginSettlement();

        try
        {
            await deadLetterPublisher.PublishAsync(
                message,
                error,
                cancellationToken);
            await channel.BasicAckAsync(
                message.DeliveryTag,
                multiple: false,
                cancellationToken);
            CompleteSettlement();
        }
        catch
        {
            ResetSettlement();
            throw;
        }
    }

    public async ValueTask AbandonAsync(CancellationToken cancellationToken = default)
    {
        await ReleaseAsync(cancellationToken);
    }

    private async ValueTask ReleaseAsync(CancellationToken cancellationToken)
    {
        BeginSettlement();

        try
        {
            await channel.BasicNackAsync(
                message.DeliveryTag,
                multiple: false,
                requeue: true,
                cancellationToken);
            CompleteSettlement();
        }
        catch
        {
            ResetSettlement();
            throw;
        }
    }

    private void BeginSettlement()
    {
        var previousState = Interlocked.CompareExchange(ref settlementState, 1, 0);

        if (previousState != 0)
        {
            throw new InvalidOperationException(
                "The RabbitMQ command delivery has already been settled.");
        }
    }

    private void CompleteSettlement()
    {
        Volatile.Write(ref settlementState, 2);
    }

    private void ResetSettlement()
    {
        Volatile.Write(ref settlementState, 0);
    }
}
