namespace TinyBus.Tests;

internal sealed class NoOpIncomingMessagePipeline : IIncomingMessagePipeline
{
    public ValueTask ExecuteAsync(
        MessageEnvelope message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.CompletedTask;
    }
}
