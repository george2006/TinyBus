# Incoming Pipeline

Every received command passes through one generated pipeline before its handler runs.

```text
MessageEnvelope
  -> middleware ordered by IncomingMiddleware.Order
  -> generated contract dispatch
  -> JSON deserialization
  -> scoped ICommandHandler<TCommand>
```

The pipeline operates on `MessageEnvelope` because transport delivery begins before TinyBus knows the
CLR command type. The generated final dispatch performs that conversion at the narrowest boundary.

## Declaring middleware

Implement `IIncomingMessageMiddleware` and give the class a unique order:

```csharp
using TinyBus;

[IncomingMiddleware(100)]
internal sealed class LoggingMiddleware : IIncomingMessageMiddleware
{
    public async ValueTask InvokeAsync(
        MessageEnvelope message,
        IIncomingMessagePipelineRuntime runtime,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Starting {message.Contract}");

        await runtime.NextAsync(message, cancellationToken);

        Console.WriteLine($"Completed {message.Contract}");
    }
}
```

Smaller order values run first. The example runs code both before and after the rest of the pipeline.

Middleware classes are registered as scoped services. Use constructor injection for application
dependencies:

```csharp
[IncomingMiddleware(200)]
internal sealed class TenantMiddleware : IIncomingMessageMiddleware
{
    private readonly TenantContext tenant;

    public TenantMiddleware(TenantContext tenant)
    {
        this.tenant = tenant;
    }

    public ValueTask InvokeAsync(
        MessageEnvelope message,
        IIncomingMessagePipelineRuntime runtime,
        CancellationToken cancellationToken)
    {
        tenant.ReadFrom(message.Headers);
        var next = runtime.NextAsync(message, cancellationToken);

        return next;
    }
}
```

## Ordering

Every order must be unique. Duplicate values produce the compile-time diagnostic `TBUS008`, listing
the middleware types in conflict.

Given middleware at orders `100` and `200`, execution is deterministic:

```text
100 before
  200 before
    command handler
  200 after
100 after
```

The source generator writes this chain as direct method calls. There is no runtime middleware scan or
delegate pipeline construction.

## Short-circuiting

A middleware can stop command execution by returning without calling `runtime.NextAsync`:

```csharp
[IncomingMiddleware(50)]
internal sealed class MaintenanceMiddleware : IIncomingMessageMiddleware
{
    public ValueTask InvokeAsync(
        MessageEnvelope message,
        IIncomingMessagePipelineRuntime runtime,
        CancellationToken cancellationToken)
    {
        if (MaintenanceState.IsEnabled)
        {
            return ValueTask.CompletedTask;
        }

        var next = runtime.NextAsync(message, cancellationToken);
        return next;
    }
}
```

Returning successfully tells the runtime that the delivery completed. Throw an exception when the
delivery must enter retry handling.

## Scope and lifetime

The generated pipeline creates one DI scope per delivery. Middleware and the command handler resolve
from that same scope, which gives them one shared scoped lifetime for the attempt.

A retry is a new delivery attempt and therefore receives a new scope and new scoped instances.

## Failure behavior

Exceptions flow out of middleware or the handler to the common runtime. The runtime applies the retry
policy, then asks the transport delivery to schedule a retry or move itself to dead-letter storage.

Caller cancellation during host shutdown abandons the active delivery instead of counting it as a
handler failure.

## Compile-time validation

The generator rejects a decorated type that is abstract, is not a class, or does not implement
`IIncomingMessageMiddleware`. If middleware validation fails, TinyBus reports the diagnostic and does
not emit the pipeline.
