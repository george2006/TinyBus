using TinyBus;

namespace TinyBus.Sample.Host;

[BusContract("commerce.rebuild")]
internal sealed record RebuildReadModel;

internal sealed class RebuildReadModelHandler : ICommandHandler<RebuildReadModel>
{
    public ValueTask HandleAsync(RebuildReadModel command, CancellationToken cancellationToken)
    {
        Console.WriteLine("Host handled RebuildReadModel.");
        return ValueTask.CompletedTask;
    }
}
