using System;
using System.Threading;
using System.Threading.Tasks;

namespace TinyBus;

public interface ITransportDelivery
{
    MessageEnvelope Envelope { get; }

    ValueTask CompleteAsync(CancellationToken cancellationToken = default);

    ValueTask FailAsync(
        Exception error,
        CancellationToken cancellationToken = default);

    ValueTask AbandonAsync(CancellationToken cancellationToken = default);
}
