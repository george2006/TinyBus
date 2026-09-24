using System.Threading;
using System.Threading.Tasks;

namespace TinyBus;

public interface ICommandHandler<TCommand>
{
    ValueTask HandleAsync(
        TCommand command,
        CancellationToken cancellationToken);
}
