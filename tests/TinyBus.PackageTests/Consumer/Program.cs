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

var options = new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true };
using var provider = services.BuildServiceProvider(options);
using var scope = provider.CreateScope();
using var otherScope = provider.CreateScope();
var executor = new CommandExecutor(scope.ServiceProvider);
var paymentId = Guid.NewGuid();
var payment = new Payments.CapturePayment(paymentId);

await executor.ExecuteAsync(payment);

var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<Payments.CapturePayment>>();
var paymentHandler = (Payments.CapturePaymentHandler)handler;
var otherHandler = otherScope.ServiceProvider.GetRequiredService<ICommandHandler<Payments.CapturePayment>>();
var otherPaymentHandler = (Payments.CapturePaymentHandler)otherHandler;
var receivedSameCommand = ReferenceEquals(payment, paymentHandler.LastCommand);

Require(receivedSameCommand, "Command reaches the scoped handler unchanged");
Require(paymentHandler.CallCount == 1, "Command executes once");
Require(otherPaymentHandler.CallCount == 0, "Other scope remains untouched");

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
        public CapturePayment? LastCommand { get; private set; }
        public int CallCount { get; private set; }

        public ValueTask HandleAsync(
            CapturePayment command,
            CancellationToken cancellationToken)
        {
            LastCommand = command;
            CallCount++;
            return ValueTask.CompletedTask;
        }
    }
}
