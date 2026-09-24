using System.Threading;
using System.Threading.Tasks;

namespace TinyBus;

public interface IEventHandler<TEvent>
{
    ValueTask HandleAsync(
        TEvent @event,
        CancellationToken cancellationToken);
}
