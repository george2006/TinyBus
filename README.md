# TinyBus

TinyBus is a compile-time-oriented messaging library for .NET. Ordinary records and handler
declarations become a deterministic service manifest through source generation, without runtime
assembly scanning or marker interfaces on messages.

```csharp
using TinyBus;

public sealed record CapturePayment(Guid PaymentId, decimal Amount);

public sealed class CapturePaymentHandler : ICommandHandler<CapturePayment>
{
    public ValueTask HandleAsync(
        CapturePayment command,
        CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
```

In the root application, combine the generated manifest with an explicit service identity:

```csharp
var service = new ServiceIdentity("payments");
var manifest = new TinyBus.Generated.GeneratedTinyBusManifest();
var topology = manifest.CreateTopology(service);
```

The topology includes local handlers and contributions from assemblies referenced by the root
compilation. Its service identity is supplied by the application; it is not inferred from an
assembly name. This creates metadata only and does not activate handlers or start a transport.

Register the generated command and event handlers with Microsoft DI:

```csharp
var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
TinyBus.Generated.GeneratedTinyBusManifest.RegisterHandlers(services);
```

This includes local and referenced handlers, with scoped lifetimes. Repeated calls preserve each
service/implementation pair once, including all distinct event handlers. The application supplies
handler dependencies and owns service-provider scopes. Request registration and handler execution
remain later slices. TinyBus references DI abstractions; building a provider requires the application's
DI container package.

Run the composed topology sample:

```sh
dotnet run --project samples/TinyBus.Sample.Host -- commerce.demo
```

The host combines its local command with the Orders and Payments handler libraries. Its five
descriptors include both handlers for `OrderPlaced` and the response type for `GetPaymentStatus`.
The libraries keep their handlers internal; composition uses their generated public manifests.

Run `tests/TinyBus.PackageTests/Verify-Package.ps1` to verify the same sample through the NuGet package,
including cross-assembly diagnostics and compiler-only generator placement.

The current bootstrap contains the transport-independent contracts, generated manifests and
compile-time topology diagnostics. Transports, persistence, workers, retries and distributed
runtime behavior are intentionally not implemented yet.
