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

Register the generated command, event and request handlers with Microsoft DI:

```csharp
var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
TinyBus.Generated.GeneratedTinyBusManifest.RegisterHandlers(services);
```

This includes local and referenced handlers, with scoped lifetimes. Repeated calls preserve each
service/implementation pair once, including all distinct event handlers. The application supplies
handler dependencies and owns service-provider scopes.
TinyBus references DI abstractions; building a provider requires the application's
DI container package.

Execute a command through an existing scope:

```csharp
var executor = new TinyBus.CommandExecutor(scope.ServiceProvider);
await executor.ExecuteAsync(command, cancellationToken);
```

The executor resolves the registered command handler and returns its completion directly. Keep the
scope alive until execution completes. Failures and cancellation propagate to the caller. This is
local handler execution; sending through a transport remains a later slice.

Execute all registered event handlers through an existing scope:

```csharp
var executor = new TinyBus.EventExecutor(scope.ServiceProvider);
var results = await executor.ExecuteAsync(message, cancellationToken);
```

Each handler runs once, sequentially in registration order. Each EventHandlerResult records the
HandlerType and Succeeded: true on success or false on a handler exception. Failures do not stop
later handlers. Caller cancellation propagates. The executor performs no retries. Results are local
to this call; durable outcome storage and retry mechanics remain future work.

A request handler declares its response type and returns the business response:

```csharp
public sealed record GetPaymentStatus(Guid PaymentId);
public sealed record PaymentStatus(Guid PaymentId, string Status);

internal sealed class GetPaymentStatusHandler
    : IRequestHandler<GetPaymentStatus, PaymentStatus>
{
    public ValueTask<PaymentStatus> HandleAsync(
        GetPaymentStatus request,
        CancellationToken cancellationToken)
    {
        var response = new PaymentStatus(request.PaymentId, "Unknown");
        return ValueTask.FromResult(response);
    }
}
```

Generated scoped registration and internal local invocation are implemented. The intended
request/reply experience lets TinyBus turn the returned value into a correlated reply, without
requiring a reply call in the handler. Transport reply sending, reception and the caller's
IBus.RequestAsync runtime are still future work.

Run the topology and local execution sample:

```sh
dotnet run --project samples/TinyBus.Sample.Host -- commerce.demo
```

The host combines its local command with the Orders and Payments handler libraries. Its five
descriptors include both handlers for `OrderPlaced` and the response type for `GetPaymentStatus`.
The libraries keep their handlers internal; composition uses their generated public manifests.
The host then builds a validated DI scope, executes both commands and both event handlers, prints
each event outcome, and resolves the request handler to obtain its typed payment-status response.
The handlers write their invocations to the console; they do not persist payment or order state.
All execution is local to the host process. The request example calls the handler directly because
the request executor is internal and the transport request/reply runtime is not implemented yet.

Run `tests/TinyBus.PackageTests/Verify-Package.ps1` to verify the same sample through the NuGet package,
including handler execution, cross-assembly diagnostics and compiler-only generator placement.

The current implementation contains transport-independent contracts, generated manifests,
compile-time topology diagnostics, scoped registrations and local handler execution.
Transports, persistence, workers, retries and distributed
runtime behavior are intentionally not implemented yet.
