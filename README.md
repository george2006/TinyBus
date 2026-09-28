# TinyBus

TinyBus is a small, compile-time oriented service bus for .NET.

It keeps message contracts and handlers simple while moving discovery, validation, registration,
topology composition, and the incoming pipeline to generated code.

## What you get

- **Compile-time handler discovery** without runtime assembly scanning
- **Generated incoming pipelines** without delegate chains
- **Multi-assembly topology composition** owned by the host application
- **Stable wire contracts** with explicit names and versions
- **One transport per service** with PostgreSQL and RabbitMQ providers
- **Startup-gated topology** so the application is ready only after its transport is safe to use
- **Competing command consumers** with configurable concurrency
- **Delayed retries and dead-lettering** implemented by each transport
- **Native async APIs** built around `ValueTask` and `CancellationToken`

## Status

TinyBus is currently an alpha under active development.

The command path is implemented end to end for PostgreSQL and RabbitMQ: topology reconciliation,
sending, receiving, generated dispatch, retries, and dead-lettering. Event publishing and
request/reply remain under development.

## Quick start

Define a command. Add `BusContract` when its wire identity must survive CLR renames; version `1` is
used by default.

```csharp
using TinyBus;

[BusContract("payments.capture")]
public sealed record CapturePayment(Guid PaymentId, decimal Amount);
```

Implement its handler:

```csharp
using TinyBus;

internal sealed class CapturePaymentHandler : ICommandHandler<CapturePayment>
{
    public ValueTask HandleAsync(
        CapturePayment command,
        CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
```

Register one logical service and one transport:

```csharp
using TinyBus;
using TinyBus.PostgreSql;

services.AddTinyBus(bus =>
{
    bus.Service("payments");
    bus.UsePostgreSql(connectionString);
});
```

RabbitMQ uses the same TinyBus registration seam:

```csharp
using TinyBus.RabbitMq;

services.AddTinyBus(bus =>
{
    bus.Service("payments");
    bus.UseRabbitMq(connectionString);
});
```

Send the command through `IBus`:

```csharp
await bus.SendAsync(
    new CapturePayment(paymentId, 100m),
    cancellationToken);
```

The source generator finds the handler, emits its scoped registration, contributes the command to
the service topology, and generates the dispatch path. The selected transport owns the physical
routing and durable delivery mechanics.

## Retries

Retries belong to the bus policy while scheduling and dead-letter storage belong to the transport:

```csharp
services.AddTinyBus(bus =>
{
    bus.Service("payments");
    bus.MaximumConcurrentMessages = 16;

    bus.RetryOptions.MaximumAttempts = 5;
    bus.RetryOptions.MinimumDelay = TimeSpan.FromSeconds(1);
    bus.RetryOptions.MaximumDelay = TimeSpan.FromSeconds(30);

    bus.UsePostgreSql(connectionString);
});
```

Every delivery runs once per attempt. A handler exception schedules another attempt using bounded
exponential backoff. The final failure moves the command to the provider's dead-letter storage.

## Incoming middleware

Middleware works with the transport-neutral `MessageEnvelope` and is ordered explicitly:

```csharp
[IncomingMiddleware(100)]
internal sealed class LoggingMiddleware : IIncomingMessageMiddleware
{
    public async ValueTask InvokeAsync(
        MessageEnvelope message,
        IIncomingMessagePipelineRuntime runtime,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Receiving {message.Contract}");
        await runtime.NextAsync(message, cancellationToken);
    }
}
```

TinyBus generates the middleware chain and the final command dispatch. Middleware instances remain
regular DI services and may use constructor injection.

## Transport model

TinyBus Core owns the common runtime and message pipeline. Providers own the mechanics that differ
in practice:

| Provider | Command routing | Delivery storage | Retry and dead letter |
| --- | --- | --- | --- |
| PostgreSQL | Command ownership stored in shared tables | Claimed rows with competing consumers | Scheduled rows and an atomic move to the dead-letter table |
| RabbitMQ | Deterministic exchanges, routing keys, queues, and bindings | Native broker deliveries | Delayed retry queues and a durable dead-letter queue |

Both providers validate that a command has a single owning service. Topology reconciliation is
additive, so an older replica cannot erase declarations introduced by a newer replica.

## Documentation

- [Getting Started](docs/getting-started.md)
- [Architecture](docs/architecture.md)
- [Message Contracts](docs/message-contracts.md)
- [Incoming Pipeline](docs/incoming-pipeline.md)
- [Transports](docs/transports.md)
- [Retries and Dead Letters](docs/retries-and-dead-letters.md)
- [Source Generator](docs/source-generator.md)
- [Design Decisions](docs/design-decisions.md)

## Tiny suite

TinyBus belongs to the Tiny suite:

| Project | Responsibility |
| --- | --- |
| [TinyDispatcher](https://github.com/george2006/TinyDispatcher) | In-process command and query execution |
| [TinyValidations](https://github.com/george2006/TinyValidations) | Application input validation |
| [TinyEvents](https://github.com/george2006/TinyEvents) | Reliable application events through the outbox pattern |
| [TinyBus](https://github.com/george2006/TinyBus) | Durable messaging between services |

Each library can be adopted independently.

## When to use

TinyBus is a good fit when you want:

- explicit commands and events rather than a generic consumer abstraction
- compile-time discovery and validation
- generated execution paths suitable for trimming and future Native AOT support
- transport-native durability behind a small common runtime
- code whose execution flow can be read and debugged directly

## Samples

The repository contains a multi-project sample under `samples/`. It demonstrates handler discovery,
cross-assembly topology composition, and local command, event, and request execution while the
distributed examples continue to evolve.
