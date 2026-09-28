# Getting Started

This guide creates a service that owns a command, receives it through a transport, and executes its
handler through the generated incoming pipeline.

## 1. Reference TinyBus and one provider

TinyBus is currently an alpha and its packages have not been published. Reference Core and one
provider from this repository while developing with it:

```xml
<ItemGroup>
  <ProjectReference Include="..\TinyBus\src\TinyBus\TinyBus.csproj" />
  <ProjectReference Include="..\TinyBus\src\TinyBus.PostgreSql\TinyBus.PostgreSql.csproj" />
</ItemGroup>
```

For RabbitMQ, replace the provider reference:

```xml
<ProjectReference Include="..\TinyBus\src\TinyBus.RabbitMq\TinyBus.RabbitMq.csproj" />
```

The package IDs reserved by the projects are `TinySuite.TinyBus`,
`TinySuite.TinyBus.PostgreSql`, and `TinySuite.TinyBus.RabbitMq`. Installation commands will be added
when the first preview is published.

## 2. Define the command

Messages are ordinary .NET types. `BusContract` gives the message a stable wire name and version.

```csharp
using TinyBus;

[BusContract("payments.capture")]
public sealed record CapturePayment(Guid PaymentId, decimal Amount);
```

The version defaults to `1`. Set it explicitly when introducing a new wire contract:

```csharp
[BusContract("payments.capture", Version = 2)]
public sealed record CapturePaymentV2(
    Guid PaymentId,
    decimal Amount,
    string Currency);
```

## 3. Implement the handler

```csharp
using TinyBus;

internal sealed class CapturePaymentHandler : ICommandHandler<CapturePayment>
{
    public ValueTask HandleAsync(
        CapturePayment command,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Capturing {command.Amount} for {command.PaymentId}");
        return ValueTask.CompletedTask;
    }
}
```

The handler may be internal. TinyBus discovers it at compile time and registers it as scoped.
Constructor injection works through the application's service provider.

## 4. Register the service

PostgreSQL:

```csharp
using TinyBus;
using TinyBus.PostgreSql;

builder.Services.AddTinyBus(bus =>
{
    bus.Service("payments");
    bus.UsePostgreSql(connectionString);
});
```

RabbitMQ:

```csharp
using TinyBus;
using TinyBus.RabbitMq;

builder.Services.AddTinyBus(bus =>
{
    bus.Service("payments");
    bus.UseRabbitMq(connectionString);
});
```

Register TinyBus once and select exactly one provider. Host startup waits for that provider to
initialize its topology. Initialization errors fail startup instead of exposing an unready bus.

## 5. Run the host

TinyBus registers its runtime as an `IHostedService`, so it starts with the .NET Generic Host:

```csharp
var app = builder.Build();
await app.RunAsync();
```

The runtime receives up to `Environment.ProcessorCount` messages concurrently by default. Configure
the limit with the rest of the bus:

```csharp
builder.Services.AddTinyBus(bus =>
{
    bus.Service("payments");
    bus.MaximumConcurrentMessages = 16;
    bus.UsePostgreSql(connectionString);
});
```

## 6. Send a command

Resolve `IBus` in any service managed by DI:

```csharp
internal sealed class CheckoutService
{
    private readonly IBus bus;

    public CheckoutService(IBus bus)
    {
        this.bus = bus;
    }

    public ValueTask CaptureAsync(
        Guid paymentId,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var command = new CapturePayment(paymentId, amount);
        var sending = bus.SendAsync(command, cancellationToken);

        return sending;
    }
}
```

`SendAsync` creates a `MessageEnvelope`, serializes the command as JSON, and hands it to the selected
transport. Completion means the provider accepted the message according to its durability contract;
the remote handler runs asynchronously.

## What happens at build time

The source generator:

1. discovers the command handler;
2. validates its shape and contract identity;
3. emits its scoped DI registration;
4. contributes its command ownership to the service manifest;
5. emits the incoming dispatch path.

No runtime assembly scan is required. Referenced class libraries can contribute handlers, and the
host application composes the final service manifest.

Continue with [Architecture](architecture.md) for the runtime flow, or [Retries and Dead Letters](retries-and-dead-letters.md)
for failure behavior.
