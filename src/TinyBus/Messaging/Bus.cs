using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TinyBus;

internal sealed class Bus : IBus
{
    private readonly ITransport transport;

    public Bus(ITransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);

        this.transport = transport;
    }

    public ValueTask SendAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var contract = CommandContract<TCommand>.ReadIdentity();
        var payload = JsonSerializer.Serialize(command);
        var messageId = Guid.NewGuid();
        var envelope = new MessageEnvelope(messageId, contract, payload);
        var sending = transport.SendAsync(envelope, cancellationToken);

        return sending;
    }

    public ValueTask PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Event publishing has not been implemented.");
    }

    public ValueTask<TResponse> RequestAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Request and response has not been implemented.");
    }

    private static class CommandContract<TCommand>
    {
        private static ContractIdentity identity;
        private static bool initialized;

        internal static ContractIdentity ReadIdentity()
        {
            if (Volatile.Read(ref initialized))
            {
                return identity;
            }

            var messageType = typeof(TCommand);
            var resolvedIdentity = ContractIdentity.From(messageType);
            identity = resolvedIdentity;
            Volatile.Write(ref initialized, true);

            return resolvedIdentity;
        }
    }
}
