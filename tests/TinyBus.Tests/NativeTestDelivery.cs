namespace TinyBus.Tests;

internal sealed class NativeTestDelivery : ITransportDelivery
{
    private readonly MessageEnvelope? envelope;
    private readonly Exception? envelopeError;
    private readonly TaskCompletionSource completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource abandoned =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<ScheduledRetry> retryScheduled =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<Exception> deadLettered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal NativeTestDelivery(MessageEnvelope envelope, int attempt = 1)
    {
        this.envelope = envelope;
        MessageId = envelope.MessageId;
        Attempt = attempt;
    }

    internal NativeTestDelivery(
        Guid? messageId,
        Exception envelopeError,
        int attempt = 1)
    {
        ArgumentNullException.ThrowIfNull(envelopeError);

        MessageId = messageId;
        this.envelopeError = envelopeError;
        Attempt = attempt;
    }

    public Guid? MessageId { get; }

    public int Attempt { get; }

    internal Task Completed => completed.Task;

    internal Task Abandoned => abandoned.Task;

    internal Task<ScheduledRetry> RetryScheduled => retryScheduled.Task;

    internal Task<Exception> DeadLettered => deadLettered.Task;

    public MessageEnvelope ReadEnvelope()
    {
        if (envelopeError is not null)
        {
            throw envelopeError;
        }

        return envelope!;
    }

    public ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        completed.TrySetResult();

        return ValueTask.CompletedTask;
    }

    public ValueTask ScheduleRetryAsync(
        Exception error,
        TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(error);
        cancellationToken.ThrowIfCancellationRequested();
        var retry = new ScheduledRetry(error, delay);
        retryScheduled.TrySetResult(retry);

        return ValueTask.CompletedTask;
    }

    public ValueTask DeadLetterAsync(
        Exception error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(error);
        cancellationToken.ThrowIfCancellationRequested();
        deadLettered.TrySetResult(error);

        return ValueTask.CompletedTask;
    }

    public ValueTask AbandonAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        abandoned.TrySetResult();

        return ValueTask.CompletedTask;
    }
}

internal sealed record ScheduledRetry(Exception Error, TimeSpan Delay);
