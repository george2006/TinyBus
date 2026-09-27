using System.Threading;
using System.Threading.Tasks;

namespace TinyBus;

public interface IIncomingMessagePipeline
{
    ValueTask ExecuteAsync(
        MessageEnvelope message,
        CancellationToken cancellationToken = default);
}
