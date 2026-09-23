using TinyBus;

var messages = new TinyBus.Generated.GeneratedTinyBusManifest().Messages;

Require(messages.Count == 1, "One command descriptor");

var command = messages[0];
Require(command.Contract == new ContractIdentity("Payments.CapturePayment", 1), "Contract identity");
Require(command.MessageType == typeof(Payments.CapturePayment), "Message type");
Require(command.HandlerType == typeof(Payments.CapturePaymentHandler), "Handler type");
Require(command.Kind == MessageKind.Command, "Message kind");
Require(command.ResponseType is null, "Command has no response type");
Require(
    !File.Exists(Path.Combine(AppContext.BaseDirectory, "TinyBus.SourceGen.dll")),
    "Generator remains a compiler asset");

Console.WriteLine("TinyBus package consumer passed.");

static void Require(bool condition, string behavior)
{
    if (!condition)
    {
        throw new InvalidOperationException(behavior);
    }
}

namespace Payments
{
    public sealed record CapturePayment(Guid PaymentId);

    public sealed class CapturePaymentHandler : ICommandHandler<CapturePayment>
    {
        public ValueTask HandleAsync(
            CapturePayment command,
            CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }
}
