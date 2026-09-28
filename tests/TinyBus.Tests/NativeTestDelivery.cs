namespace TinyBus.Tests;

internal sealed class NativeTestDelivery : ITransportDelivery
{
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
        Envelope = envelope;
        Attempt = attempt;
    }

    public MessageEnvelope Envelope { get; }

    public int Attempt { get; }

    internal Task Completed => completed.Task;

    internal Task Abandoned => abandoned.Task;

    internal Task<ScheduledRetry> RetryScheduled => retryScheduled.Task;

    internal Task<Exception> DeadLettered => deadLettered.Task;

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
