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

In the root application, register TinyBus with its logical service identity:

```csharp
using Microsoft.Extensions.DependencyInjection;
using TinyBus;

services.AddTinyBus(bus =>
{
    bus.Service("payments");
});
```

The generated entry point binds the root application's composed manifest, including referenced
handler libraries. It registers ServiceTopology, scoped handlers and the common TinyBus runtime.
The callback runs during registration. Configure one TinyBus runtime per service
collection; a second AddTinyBus call is rejected. The application supplies handler dependencies
and owns service-provider scopes.

Host startup requires exactly one ITransport in DI. TinyBus awaits its InitializeAsync operation
before the host becomes ready; missing, duplicate or failed transport initialization prevents startup.
AddTinyBus does not create a command route cache or require route discovery. Transport packages extend
TinyBusOptions and register their implementation through its Services collection. The planned selection
methods are UsePostgreSql and UseRabbitMq. Their projects are scaffolded; the provider implementations
and those methods are not available yet.
Outbound requirements will be inferred from IBus usage in a later generator slice; this registration
slice currently supplies no outbound requirements and does not implement IBus sending.
TinyBus references DI and Hosting abstractions; applications supply their DI container and host.

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
The sample exercises AddTinyBus registration and local execution through a service provider; it does
not start a Generic Host or the topology worker without a topology provider.

Run `tests/TinyBus.PackageTests/Verify-Package.ps1` to verify the same sample through the NuGet package,
including handler execution, cross-assembly diagnostics and compiler-only generator placement.

The current implementation contains transport-independent contracts, generated manifests,
compile-time topology diagnostics, scoped registrations and local handler execution.
`ITransport.InitializeAsync` is the provider startup seam. The internal TinyBus runtime awaits exactly
one transport before host readiness. PostgreSQL owns its internal command-route fact and immutable
cache; RabbitMQ does not implement or register them. Only test transport implementations exist so far.
Production providers remain future work.
Core, PostgreSQL and RabbitMQ have separate test projects so provider mechanics cannot leak into the
Core test dependency graph. RabbitMQ tests begin when its first concrete behavior is implemented.
Transports, persistence, background refresh, receive workers and retries are not implemented yet.
