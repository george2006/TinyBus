using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace TinyBus;

[EditorBrowsable(EditorBrowsableState.Never)]
public interface IIncomingMessagePipeline
{
    ValueTask ExecuteAsync(
        MessageEnvelope message,
        CancellationToken cancellationToken = default);
}
