using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TinyBus;

/// <summary>
/// Defines the provider contract used by the TinyBus runtime.
/// Applications select an implementation through a transport package.
/// </summary>
public interface ITransport
{
    /// <summary>
    /// Reconciles the service topology and prepares the provider for safe messaging.
    /// The host does not become ready until this operation completes.
    /// </summary>
    ValueTask InitializeAsync(
        ServiceTopology topology,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a command and completes after the selected provider confirms durable acceptance.
    /// </summary>
    ValueTask SendAsync(
        MessageEnvelope message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Receives no more deliveries than the runtime's currently available capacity.
    /// </summary>
    ValueTask<IReadOnlyList<ITransportDelivery>> ReceiveAsync(
        ReceiveCapacity capacity,
        CancellationToken cancellationToken = default);
}
