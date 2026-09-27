using System.Threading;
using System.Threading.Tasks;

namespace TinyBus;

/// <summary>
/// Prepares a transport for safe messaging by this service.
/// </summary>
public interface ITransport
{
    ValueTask InitializeAsync(
        ServiceTopology topology,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a command and completes after the selected provider confirms durable acceptance.
    /// </summary>
    ValueTask SendAsync(
        MessageEnvelope message,
        CancellationToken cancellationToken = default);
}
