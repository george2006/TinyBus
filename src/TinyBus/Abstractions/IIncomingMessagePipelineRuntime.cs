using System.Threading;
using System.Threading.Tasks;

namespace TinyBus;

public interface IIncomingMessagePipelineRuntime
{
    ValueTask NextAsync(
        MessageEnvelope message,
        CancellationToken cancellationToken);
}
