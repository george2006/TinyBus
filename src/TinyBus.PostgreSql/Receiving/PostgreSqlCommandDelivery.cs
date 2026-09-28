using System;
using System.Threading;
using System.Threading.Tasks;
using TinyBus;
using TinyBus.PostgreSql.Persistence;
using TinyBus.PostgreSql.Persistence.Commands;

namespace TinyBus.PostgreSql.Receiving;

internal sealed class PostgreSqlCommandDelivery : ITransportDelivery
{
    private readonly ClaimedCommandMessage message;
    private readonly CompleteCommandMessage completeCommandMessage;
    private readonly ScheduleCommandMessageRetry scheduleCommandMessageRetry;
    private readonly DeadLetterCommandMessage deadLetterCommandMessage;
    private readonly AbandonCommandMessage abandonCommandMessage;
    private int settlementState;

    internal PostgreSqlCommandDelivery(
        ClaimedCommandMessage message,
        CompleteCommandMessage completeCommandMessage,
        ScheduleCommandMessageRetry scheduleCommandMessageRetry,
        DeadLetterCommandMessage deadLetterCommandMessage,
        AbandonCommandMessage abandonCommandMessage)
    {
        this.message = message;
        this.completeCommandMessage = completeCommandMessage;
        this.scheduleCommandMessageRetry = scheduleCommandMessageRetry;
        this.deadLetterCommandMessage = deadLetterCommandMessage;
        this.abandonCommandMessage = abandonCommandMessage;
    }

    public MessageEnvelope Envelope => message.Envelope;

    public int Attempt => message.Attempt;

    public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        BeginSettlement();

        try
        {
            var completed = await completeCommandMessage.ExecuteAsync(
                message.SequenceId,
                message.ClaimId,
                cancellationToken);
            EnsureDeliveryIsOwned(completed);
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
            var scheduled = await scheduleCommandMessageRetry.ExecuteAsync(
                message.SequenceId,
                message.ClaimId,
                delay,
                cancellationToken);
            EnsureDeliveryIsOwned(scheduled);
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
            var deadLettered = await deadLetterCommandMessage.ExecuteAsync(
                message.SequenceId,
                message.ClaimId,
                error,
                cancellationToken);
            EnsureDeliveryIsOwned(deadLettered);
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
            var abandoned = await abandonCommandMessage.ExecuteAsync(
                message.SequenceId,
                message.ClaimId,
                cancellationToken);
            EnsureDeliveryIsOwned(abandoned);
            CompleteSettlement();
        }
        catch
        {
            ResetSettlement();
            throw;
        }
    }

    private static void EnsureDeliveryIsOwned(bool settlementSucceeded)
    {
        if (!settlementSucceeded)
        {
            throw new InvalidOperationException(
                "The PostgreSQL command delivery is no longer owned by this receiver.");
        }
    }

    private void BeginSettlement()
    {
        var previousState = Interlocked.CompareExchange(ref settlementState, 1, 0);

        if (previousState != 0)
        {
            throw new InvalidOperationException(
                "The PostgreSQL command delivery has already been settled.");
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
