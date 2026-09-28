# Message Contracts

TinyBus uses ordinary .NET types as messages. Their handler interface gives each type its meaning:

| Message kind | Handler | Service topology |
| --- | --- | --- |
| Command | `ICommandHandler<TCommand>` | One handler and one owning service |
| Event | `IEventHandler<TEvent>` | Zero or more independent subscribers |
| Request | `IRequestHandler<TRequest, TResponse>` | One handler and one response type |

Distributed commands are implemented. Distributed event publishing and request/reply are still under
development.

## Commands

A command asks one service to perform work:

```csharp
public sealed record CapturePayment(Guid PaymentId, decimal Amount);

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

TinyBus reports a compile-time error when one service contains multiple handlers for the same
command. Providers validate ownership across services during startup.

## Events

An event announces a fact to every interested subscription:

```csharp
public sealed record OrderPlaced(Guid OrderId);

internal sealed class UpdateReadModel : IEventHandler<OrderPlaced>
{
    public ValueTask HandleAsync(
        OrderPlaced @event,
        CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
```

Multiple event handlers are valid. Each handler represents an independent subscription outcome, so a
future distributed event runtime can retry one failed subscriber without replaying successful ones.

## Requests

A request declares its response as part of the handler contract:

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
        var response = new PaymentStatus(request.PaymentId, "Captured");
        return ValueTask.FromResult(response);
    }
}
```

The returned value is the business response. The planned request/reply runtime will correlate and
transport it without requiring handlers to call a reply API.

## Wire identity

Every message has a `ContractIdentity`: a name and a positive integer version.

Without an attribute, TinyBus uses the fully qualified CLR type name and version `1`:

```csharp
namespace Payments.Contracts;

public sealed record CapturePayment(Guid PaymentId);

// Contract: Payments.Contracts.CapturePayment, version 1
```

This convention is convenient inside an evolving codebase, but a namespace or type rename changes
the wire contract. Public or long-lived messages should declare an explicit identity:

```csharp
[BusContract("payments.capture")]
public sealed record CapturePayment(Guid PaymentId);
```

Set `Version` only when the wire shape represents a distinct contract:

```csharp
[BusContract("payments.capture", Version = 2)]
public sealed record CapturePaymentV2(
    Guid PaymentId,
    decimal Amount,
    string Currency);
```

Arrays and generic message types require `BusContractAttribute` because their conventional CLR names
cannot be reproduced safely at runtime.

## Compile-time guarantees

The generator reports an error for:

- an empty contract name;
- a version lower than `1`;
- multiple command handlers in one composed service;
- multiple request handlers in one composed service;
- one message type used as more than one message kind;
- one contract identity assigned to different message types.

Validation includes handlers contributed by referenced assemblies, so a conflict cannot hide behind a
module boundary.

## Envelope

Transports carry a `MessageEnvelope`, which contains:

- a unique message ID;
- the contract name and version;
- the serialized JSON payload;
- optional correlation and causation IDs;
- optional application headers.

The envelope is the stable incoming pipeline boundary. The generated dispatcher converts its payload
back to the typed message immediately before invoking the handler.
