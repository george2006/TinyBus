using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace TinyBus;

/// <summary>
/// Executes commands through the caller's service provider.
/// The caller owns the scope and must keep it alive until execution completes.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CommandExecutor
{
    private readonly IServiceProvider services;

    public CommandExecutor(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        this.services = services;
    }

    public ValueTask ExecuteAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default)
    {
        var handler = services.GetRequiredService<ICommandHandler<TCommand>>();
        return handler.HandleAsync(command, cancellationToken);
    }
}
