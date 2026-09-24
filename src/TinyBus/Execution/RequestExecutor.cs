using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace TinyBus;

internal sealed class RequestExecutor
{
    private readonly IServiceProvider services;

    public RequestExecutor(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        this.services = services;
    }

    public ValueTask<TResponse> ExecuteAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        var handler = services.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
        return handler.HandleAsync(request, cancellationToken);
    }
}
