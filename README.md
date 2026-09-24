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
var topology = new TinyBus.Generated.GeneratedTinyBusManifest()
    .CreateTopology(new ServiceIdentity("payments"));
```

The topology includes local handlers and contributions from assemblies referenced by the root
compilation. Its service identity is supplied by the application; it is not inferred from an
assembly name. This creates metadata only and does not activate handlers or start a transport.

The current bootstrap contains the transport-independent contracts, generated manifests and
compile-time topology diagnostics. Transports, persistence, workers, retries and distributed
runtime behavior are intentionally not implemented yet.
