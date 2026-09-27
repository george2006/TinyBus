namespace TinyBus.Tests;

internal sealed class NativeTestDelivery : ITransportDelivery
{
    private readonly TaskCompletionSource completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource abandoned =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal NativeTestDelivery(MessageEnvelope envelope)
    {
        Envelope = envelope;
    }

    public MessageEnvelope Envelope { get; }

    internal Task Completed => completed.Task;

    internal Task Abandoned => abandoned.Task;

    public ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        completed.TrySetResult();

        return ValueTask.CompletedTask;
    }

    public ValueTask AbandonAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        abandoned.TrySetResult();

        return ValueTask.CompletedTask;
    }
}
