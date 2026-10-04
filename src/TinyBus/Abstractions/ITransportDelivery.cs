using System;
using System.Threading;
using System.Threading.Tasks;

namespace TinyBus;

/// <summary>
/// Represents one provider-owned delivery attempt and its available settlement operations.
/// </summary>
public interface ITransportDelivery
{
    /// <summary>
    /// Gets the message identity when the transport can read it independently
    /// from the complete envelope.
    /// </summary>
    Guid? MessageId { get; }

    /// <summary>
    /// Reads the transport-neutral message received by the runtime. A provider
    /// may throw when the physical delivery cannot be decoded.
    /// </summary>
    MessageEnvelope ReadEnvelope();

    /// <summary>
    /// Gets the one-based processing attempt number.
    /// </summary>
    int Attempt { get; }

    /// <summary>
    /// Settles the delivery after successful pipeline execution.
    /// </summary>
    ValueTask CompleteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the delivery available for another attempt after the requested delay.
    /// </summary>
    ValueTask ScheduleRetryAsync(
        Exception error,
        TimeSpan delay,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves an exhausted delivery to provider-owned dead-letter storage.
    /// </summary>
    ValueTask DeadLetterAsync(
        Exception error,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases an interrupted delivery without consuming another failed attempt.
    /// </summary>
    ValueTask AbandonAsync(CancellationToken cancellationToken = default);
}
