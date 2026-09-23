using System.Threading;
using System.Threading.Tasks;

namespace TinyBus;

public interface IBus
{
    ValueTask SendAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default);

    ValueTask PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default);

    ValueTask<TResponse> RequestAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default);
}
