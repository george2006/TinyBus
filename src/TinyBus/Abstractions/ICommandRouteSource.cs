using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TinyBus;

/// <summary>
/// Reads accumulated command ownership for the requested contracts during startup or explicit reload.
/// Returns a snapshot with at most one route per requested contract.
/// Missing owners are omitted. Loading failures and cancellation propagate to the caller.
/// </summary>
public interface ICommandRouteSource
{
    ValueTask<IReadOnlyCollection<CommandRoute>> LoadAsync(
        IReadOnlyCollection<ContractIdentity> requiredContracts,
        CancellationToken cancellationToken = default);
}
