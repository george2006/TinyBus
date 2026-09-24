using TinyBus;
using Microsoft.Extensions.DependencyInjection;

var manifest = new TinyBus.Generated.GeneratedTinyBusManifest();
var messages = manifest.Messages;

var services = new ServiceCollection();
TinyBus.Generated.GeneratedTinyBusManifest.RegisterHandlers(services);
TinyBus.Generated.GeneratedTinyBusManifest.RegisterHandlers(services);
Require(services.Count == 1, "Repeated registration keeps one handler binding");

var registration = services[0];
Require(registration.ServiceType == typeof(ICommandHandler<Payments.CapturePayment>), "Typed handler registration");
Require(registration.ImplementationType == typeof(Payments.CapturePaymentHandler), "Concrete handler registration");
Require(registration.Lifetime == ServiceLifetime.Scoped, "Scoped handler lifetime");

Require(messages.Count == 1, "One command descriptor");

var command = messages[0];
var expectedContract = new ContractIdentity("Payments.CapturePayment", 1);
Require(command.Contract == expectedContract, "Contract identity");
Require(command.MessageType == typeof(Payments.CapturePayment), "Message type");
Require(command.HandlerType == typeof(Payments.CapturePaymentHandler), "Handler type");
Require(command.Kind == MessageKind.Command, "Message kind");
Require(command.ResponseType is null, "Command has no response type");
var generatorPath = Path.Combine(AppContext.BaseDirectory, "TinyBus.SourceGen.dll");
var generatorIsRuntimeAsset = File.Exists(generatorPath);
Require(!generatorIsRuntimeAsset, "Generator remains a compiler asset");

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
