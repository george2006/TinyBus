using System;
using System.Threading;
using System.Threading.Tasks;

namespace TinyBus;

public interface ITransportDelivery
{
    MessageEnvelope Envelope { get; }

    int Attempt { get; }

    ValueTask CompleteAsync(CancellationToken cancellationToken = default);

    ValueTask ScheduleRetryAsync(
        Exception error,
        TimeSpan delay,
        CancellationToken cancellationToken = default);

    ValueTask DeadLetterAsync(
        Exception error,
        CancellationToken cancellationToken = default);

    ValueTask AbandonAsync(CancellationToken cancellationToken = default);
}
