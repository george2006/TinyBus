using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace TinyBus;

/// <summary>
/// Attempts each event handler once in registration order and reports success by handler type.
/// The caller owns the scope and must keep it alive until execution completes.
/// </summary>
internal sealed class EventExecutor
{
    private readonly IServiceProvider services;

    public EventExecutor(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        this.services = services;
    }

    public async ValueTask<IReadOnlyList<EventHandlerResult>> ExecuteAsync<TEvent>(
        TEvent message,
        CancellationToken cancellationToken = default)
    {
        var handlers = services.GetServices<IEventHandler<TEvent>>();
        var results = new List<EventHandlerResult>();

        foreach (var handler in handlers)
        {
            var handlerType = handler.GetType();
            var succeeded = true;

            try
            {
                var completion = handler.HandleAsync(message, cancellationToken);
                await completion.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                succeeded = false;
            }

            var result = new EventHandlerResult(handlerType, succeeded);
            results.Add(result);
        }

        return results;
    }
}
