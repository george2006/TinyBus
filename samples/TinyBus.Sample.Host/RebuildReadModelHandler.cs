using TinyBus;

namespace TinyBus.Sample.Host;

[BusContract("commerce.rebuild")]
internal sealed record RebuildReadModel;

internal sealed class RebuildReadModelHandler : ICommandHandler<RebuildReadModel>
{
    public ValueTask HandleAsync(RebuildReadModel command, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
