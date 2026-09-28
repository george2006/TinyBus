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
        MessageEnvelope envelope)
    {
        this.channel = channel;
        this.deliveryTag = deliveryTag;
        Envelope = envelope;
    }

    public MessageEnvelope Envelope { get; }

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

    public async ValueTask FailAsync(
        Exception error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(error);
        await ReleaseAsync(cancellationToken);
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
