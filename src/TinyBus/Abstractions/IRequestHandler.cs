using System.Threading;
using System.Threading.Tasks;

namespace TinyBus;

public interface IRequestHandler<TRequest, TResponse>
{
    ValueTask<TResponse> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken);
}
