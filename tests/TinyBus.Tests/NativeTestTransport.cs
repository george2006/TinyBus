namespace TinyBus.Tests;

internal sealed class NativeTestTransport : ITransport
{
    public Task Availability { get; set; } = Task.CompletedTask;

    public ServiceTopology? InitializedTopology { get; private set; }

    public MessageEnvelope? SentMessage { get; private set; }

    public async ValueTask InitializeAsync(
        ServiceTopology topology,
        CancellationToken cancellationToken = default)
    {
        var availability = Availability.WaitAsync(cancellationToken);
        await availability.ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        InitializedTopology = topology;
    }

    public ValueTask SendAsync(
        MessageEnvelope message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SentMessage = message;

        return ValueTask.CompletedTask;
    }
}
