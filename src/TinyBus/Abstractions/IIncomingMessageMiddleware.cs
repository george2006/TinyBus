using System.Threading;
using System.Threading.Tasks;

namespace TinyBus;

public interface IIncomingMessageMiddleware
{
    ValueTask InvokeAsync(
        MessageEnvelope message,
        IIncomingMessagePipelineRuntime runtime,
        CancellationToken cancellationToken);
}
