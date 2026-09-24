using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace TinyBus;

internal sealed class TinyBusRuntime : BackgroundService
{
    private readonly ITransport transport;
    private readonly ServiceTopology topology;

    public TinyBusRuntime(
        ITransport transport,
        ServiceTopology topology)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(topology);

        this.transport = transport;
        this.topology = topology;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var initialization = transport.InitializeAsync(topology, cancellationToken);
        await initialization.ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        var starting = base.StartAsync(cancellationToken);
        await starting.ConfigureAwait(false);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Receiving is introduced with the transport receive contract.
        return Task.CompletedTask;
    }
}
