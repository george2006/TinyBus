using System;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using TinyBus;

namespace TinyBus.RabbitMq.Receiving;

internal sealed class RabbitMqCommandDelivery : ITransportDelivery
{
    private readonly IChannel channel;
    private readonly ulong deliveryTag;
    private int settlementState;

    internal RabbitMqCommandDelivery(
        IChannel channel,
        ulong deliveryTag,
        int attempt,
        MessageEnvelope envelope)
    {
        this.channel = channel;
        this.deliveryTag = deliveryTag;
        Attempt = attempt;
        Envelope = envelope;
    }

    public MessageEnvelope Envelope { get; }

    public int Attempt { get; }

    public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        BeginSettlement();

        try
        {
            await channel.BasicAckAsync(
                deliveryTag,
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
                deliveryTag,
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

    public ValueTask DeadLetterAsync(
        Exception error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(error);

        throw new NotSupportedException(
            "RabbitMQ dead-letter routing has not been implemented.");
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
                deliveryTag,
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
