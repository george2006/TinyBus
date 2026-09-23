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

The current bootstrap contains the transport-independent contracts, generated manifests and
compile-time topology diagnostics. Transports, persistence, workers, retries and distributed
runtime behavior are intentionally not implemented yet.
