# Source Generator

TinyBus finalizes handler registration, service topology, and incoming command dispatch at build time.
The generator ships as an analyzer inside the Core package; applications do not register it
separately.

## Generation phases

The generator follows four explicit phases:

1. **Discovery** finds handler interfaces and decorated incoming middleware in source.
2. **Analysis** reads their symbols and referenced assembly contributions.
3. **Validation** checks message contracts, service topology, and middleware ordering.
4. **Generation** emits the manifest and incoming pipeline only when validation succeeds.

Compiler errors stop generation. TinyBus does not produce a partial runtime graph from invalid
application declarations.

## Generated sources

Each compilation receives two files:

| Source | Responsibility |
| --- | --- |
| `TinyBus.Generated.Manifest.g.cs` | Message descriptors, assembly contributions, handler registration, and the generated `AddTinyBus` entry point |
| `TinyBus.Generated.IncomingPipeline.g.cs` | Middleware composition, command lookup, deserialization, and handler execution |

The generated code uses ordinary classes and direct calls. It is intended to remain understandable
when inspected in an IDE or debugger.

## Handler discovery

A concrete class becomes a TinyBus handler by implementing one of these interfaces:

```csharp
ICommandHandler<TCommand>
IEventHandler<TEvent>
IRequestHandler<TRequest, TResponse>
```

No assembly marker or handler registration call is required. Generated registrations are scoped and
support internal handler and message types.

## Multi-assembly composition

A class library analyzes only its local handlers and emits:

- a public generated local manifest;
- one assembly contribution for each local message handler;
- a method that registers its local handlers.

Those contributions contain compile-time facts: contract identity, message type, handler type,
message kind, response type, and the local manifest type.

The root application reads contributions from referenced assemblies, validates them together with its
local handlers, and emits one composed manifest. It also calls each contributing manifest's generated
registration method.

This lets a module keep handlers internal while allowing the host to own the final service topology.
The root does not copy a referenced assembly's transitive contributions, so every handler enters the
composition once.

```text
Payments assembly ----> local manifest --+
Orders assembly ------> local manifest ---+--> host composed manifest
Host source ----------> local manifest ---+
```

## Generated registration

The root compilation emits the application-facing overload:

```csharp
services.AddTinyBus(bus =>
{
    bus.Service("payments");
    bus.UsePostgreSql(connectionString);
});
```

That overload selects the host's composed manifest and registers:

- local and referenced handlers;
- decorated incoming middleware;
- the generated incoming pipeline;
- the topology, retry policy, bus, and common hosted runtime.

Applications should use this overload rather than selecting an `IBusManifest` manually.

## Incoming pipeline generation

Classes decorated with `IncomingMiddlewareAttribute` are sorted by their unique numeric order. The
generator emits their direct invocation chain and then one contract comparison per command in the
composed manifest.

When a contract matches, the generated code:

1. deserializes the JSON payload to its command type;
2. resolves the scoped `ICommandHandler<TCommand>`;
3. invokes `HandleAsync` directly.

An unknown incoming command contract fails explicitly. There is no fallback runtime scan.

## Diagnostics

| ID | Meaning |
| --- | --- |
| `TBUS001` | Contract name is empty |
| `TBUS002` | Contract version is lower than `1` |
| `TBUS003` | A command has multiple handlers in the composed service |
| `TBUS004` | A request has multiple handlers in the composed service |
| `TBUS005` | One message type has conflicting command, event, or request semantics |
| `TBUS006` | One contract identity refers to multiple message types |
| `TBUS007` | A decorated middleware type has an invalid shape |
| `TBUS008` | Multiple middleware types use the same order |

Cross-assembly diagnostics name every participating assembly and handler when source locations are not
available in the root compilation.

## Runtime boundary

The source generator discovers and composes application structure. The runtime executes the generated
result and never repeats that discovery.

Assembly contribution attributes are generated infrastructure. Applications should declare handlers
and middleware rather than constructing those attributes by hand.
