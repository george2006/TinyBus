using System.Threading;
using System.Threading.Tasks;

namespace TinyBus;

/// <summary>
/// Writes or confirms the calling service's capabilities in shared transport infrastructure.
/// Absent declarations never imply deletion; conflicting command owners must be rejected.
/// </summary>
public interface ITopologyReconciler
{
    ValueTask ReconcileAsync(
        ServiceTopology topology,
        CancellationToken cancellationToken = default);
}
