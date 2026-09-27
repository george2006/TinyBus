using System.Threading.Channels;

namespace TinyBus.Tests;

internal sealed class NativeTestTransport : ITransport
{
    private readonly Channel<ITransportDelivery> incoming =
        Channel.CreateUnbounded<ITransportDelivery>();

    public Task Availability { get; set; } = Task.CompletedTask;

    public ServiceTopology? InitializedTopology { get; private set; }

    public MessageEnvelope? SentMessage { get; private set; }

    public List<ReceiveCapacity> ReceivedCapacities { get; } = [];

    public void Enqueue(ITransportDelivery delivery)
    {
        if (!incoming.Writer.TryWrite(delivery))
        {
            throw new InvalidOperationException("The test transport cannot accept the delivery.");
        }
    }

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

    public async ValueTask<IReadOnlyList<ITransportDelivery>> ReceiveAsync(
        ReceiveCapacity capacity,
        CancellationToken cancellationToken = default)
    {
        ReceivedCapacities.Add(capacity);
        var deliveries = new List<ITransportDelivery>(capacity.Available);
        var firstDelivery = await incoming.Reader.ReadAsync(cancellationToken);
        deliveries.Add(firstDelivery);

        while (deliveries.Count < capacity.Available
            && incoming.Reader.TryRead(out var delivery))
        {
            deliveries.Add(delivery);
        }

        return deliveries;
    }
}
