# TinyBus runtime working plan

This document preserves the agreed direction after the core and source-generator bootstrap. It is
a working plan, not authorization to implement all phases at once. Each feature is sliced and
reviewed before code begins.

## Decisions

### TinyBus owns handler execution

TinyDispatcher stays outside the TinyBus consumption path. TinyDispatcher models in-process
request/handler dispatch; it does not own the distributed semantics of commands, events,
subscriptions, delivery attempts or acknowledgements.

TinyBus will execute these handlers itself:

```text
ICommandHandler<TCommand>
IEventHandler<TEvent>
IRequestHandler<TRequest, TResponse>
```

The source generator may later emit typed invocation plumbing once the runtime has a concrete need
for it. No runtime reflection or TinyDispatcher adapter is planned for handler execution.

### Handler context — future work

Handlers will need a context for the current message and delivery. Candidate information includes
message ID, correlation, causation, headers and delivery details. The context should also provide
an injected publishing capability so a handler can publish through TinyBus's outbound path.

Before implementation, agree how the context reaches handlers, its lifetime and allocation cost,
and how publishing propagates correlation and causation. Transaction and retry behavior must be
explicit; publishing from a handler does not itself guarantee atomicity or exactly-once delivery.
Design this before stabilizing the handler API. Context and publishing remain future intent and
do not change the handler signatures in the current registration slice.

### Middleware pipeline — future work

TinyBus will need middleware around handler execution. Design a smaller pipeline than
TinyDispatcher's when there is a concrete middleware use case; the current registration slice
introduces no pipeline API, continuation object or middleware registrations.

Before implementation, agree ordering, short-circuiting, scope ownership, exceptions and
cancellation, and how the pipeline surrounds event handlers and acknowledgement boundaries.
Its relationship to handler context and its allocation cost must be explicit. Preserve direct
typed calls and avoid delegate chains. Concrete middleware types and pipeline mechanics remain
deferred decisions.

## Feature order

### 1. Multi-assembly topology composition

Each consuming assembly already generates its local manifest. A root service must compose local and
referenced contributions without runtime assembly scanning.

Planned slices:

1. Emit portable compile-time contribution metadata and a uniquely named generated manifest per
   assembly. Implemented, verified and approved.
2. Read referenced contributions inside source-generator analysis and convert all Roslyn symbols
   to TinyBus-owned models at that boundary. Implemented, verified and approved.
3. Generate one deterministic composed manifest in the root assembly. Implemented, verified and
   approved.
4. Report duplicate command/request handlers and conflicting semantics across assemblies.
   Implemented, verified and approved.
5. Combine the composed manifest with an explicitly supplied `ServiceIdentity` to create
   `ServiceTopology`. Implemented, verified and approved.
6. Prove the behavior with a real host plus multiple handler libraries and a packaged consumer.
   Implemented, verified and approved.

Implementation details, verification and approval history live in [`PLAN.md`](../PLAN.md).
Multi-assembly topology and the generator refinements are approved. Native handler activation and
invocation is the current feature; registration and typed command execution are implemented.

Approved seam:

```csharp
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class BusMessageContributionAttribute : Attribute
{
    // Compiler metadata emitted by TinyBus.SourceGen; users do not declare it manually.
}
```

The simpler alternative is the TinyFlags-style static registry populated by module initializers.
That carries global mutable state and makes completeness depend on runtime assembly loading, so the
compile-time contribution approach is preferred for bus topology.

Decision approved: TinyBus intentionally uses this compile-time contribution model even though
TinyFlags uses a runtime bootstrap registry. Distributed message topology needs stronger
completeness and cross-assembly validation guarantees than feature-definition aggregation.

### 2. Native handler activation and invocation

Define how generated code resolves and invokes handlers through the host service provider.
TinyBus owns acknowledgement boundaries:

- successful handler completion allows acknowledgement
- handler failure reaches the delivery runtime and causes retry/failure handling
- an event invokes every local event handler represented by the service topology
- a command or request has exactly one local handler, already enforced by diagnostics
- cancellation remains distinct from processing failure

Handler execution must use no invocation delegates or runtime reflection. Any new invocation type
must have a concrete runtime consumer and be discussed before implementation.

Proposed slices:

1. Generate command and event handler DI registrations, including referenced assemblies. Prove
   resolution and repeated registration without adding execution behavior.
2. Execute a typed command through the caller's scoped service provider, propagating completion,
   failures and cancellation. Measure TinyBus dispatch allocations.
3. Execute local event handlers; agree ordering and failure behavior before implementing this slice.
4. Register and execute requests and return their responses.
5. Extend the host and packaged consumer to prove execution across assembly boundaries.

#### Slice 1: command and event handler registration — implemented, verified and approved

Generated manifests now register bindings
from `ICommandHandler<TCommand>` and `IEventHandler<TEvent>` to each concrete handler in its owning
assembly. The static local entry point is `RegisterLocalHandlers(IServiceCollection services)`
on each public assembly manifest. The root's `RegisterHandlers(IServiceCollection services)` calls
the local and distinct referenced registration methods, preserving access to internal handlers.

Follow the typed registration approach inspected in TinyDispatcher, with explicit names and
registration that does not duplicate the same service/implementation pair when called repeatedly.
Preserve every distinct event handler. Keep root composition explicit; no module-initializer
registry, assembly scanning or registration delegates are needed.

Approved choices: scoped handler lifetime and Microsoft.Extensions.DependencyInjection.Abstractions
in TinyBus core. Registrations use typed service/implementation descriptors and TryAddEnumerable.
No new runtime executor or handler context is part of this slice. Request registration remains
with request execution in its later slice.

Verify real service-provider resolution for local and referenced internal handlers, all handlers
for a shared event, repeated registration and overlapping assembly references, the chosen lifetime,
and an empty assembly. Existing topology diagnostics continue to gate generation.

#### Slice 2: typed command execution — implemented, verified and approved

Existing generated registrations bind command handler interfaces to concrete scoped handlers.
The approved addition is a concrete `CommandExecutor` in TinyBus core, constructed with the
caller's scoped `IServiceProvider`. Its single operation is:

```csharp
ValueTask ExecuteAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
```

It resolves a handler for each call from that provider and invokes it directly. The caller owns
the scope and keeps it alive until execution completes. The executor holds the provider, not a
cached handler, and introduces one allocation when constructed. No executor interface or automatic
executor registration is introduced. Its contract and implementation are approved.

The simpler alternative was to put resolution and invocation in each caller. The concrete executor
gives callers one TinyBus operation for command execution. This is inbound local execution;
the existing IBus SendAsync contract remains part of the future outbound path.

Verify real scoped resolution through generated registrations, exact command/token forwarding,
synchronous and asynchronous completion, synchronous and asynchronous failures, cancellation and
missing registration. Pass cancellation to the handler and preserve its outcome. No scope creation,
retry, acknowledgement, event/request execution, handler context or middleware is added here.

Resolve `ICommandHandler<TCommand>` and return its
`HandleAsync` result directly. Keep messages typed and avoid an extra async wrapper. The allocation
requirement is zero TinyBus dispatch allocations per successful command after initialization,
verified by measurement. Record synchronous and asynchronous completion separately, identifying
handler work, DI activation and scope costs separately. Registration allocations occur at startup.

Verification: all sixty-six tests pass in Release, including generated registration with an
internal handler. Allocation tests measure zero bytes over 10,000 warmed calls for synchronous
completion and for returning a pending handler-owned task. They measure dispatch on the calling
thread, excluding scope creation, initial resolution and handler-owned task/continuation work.

### 3. Transport-independent outbound and inbound boundaries

Design the smallest concrete transport seam for TinyBus outbound operations and native delivery.
The design must define:

- acceptance of a `MessageEnvelope`
- command, event and request semantics
- acknowledgement and failure boundaries
- idempotency by `MessageId`
- serialization ownership
- correlation and causation propagation

No transport interface is introduced until its PostgreSQL implementation and the TinyBus runtime
operations that consume it are understood concretely.

The transport boundary must also be implementable by a second, structurally different transport.
It cannot expose PostgreSQL concepts such as tables, rows, polling, leases, `LISTEN/NOTIFY` or
`FOR UPDATE SKIP LOCKED`. Those belong exclusively to the PostgreSQL package. Before stabilizing the
boundary, validate its shape conceptually against at least one broker transport such as RabbitMQ or
Azure Service Bus so PostgreSQL does not accidentally become the abstraction.

Mandatory checkpoint: once the first transport interfaces are drafted, stop before implementing
`TinyBus.PostgreSql`. Map the exact same contracts against PostgreSQL and at least one broker
transport. Review command routing, event fan-out, acknowledgement, redelivery, retry ownership,
idempotency and request/response without changing the common vocabulary. If a contract only makes
sense through rows, polling or database leases, redesign it before any provider code begins. Record
the comparison and approval in this document before passing the checkpoint.

### 4. TinyBus.PostgreSql

PostgreSQL is the first native distributed transport, not the TinyBus runtime model. TinyBus must
support additional transports through the same transport-independent semantics.

Within `TinyBus.PostgreSql`, durable tables are the source of truth. If `LISTEN/NOTIFY` is included,
it is an optional low-latency wake-up optimization; polling remains the recovery path and correctness
must never depend on receiving a notification. The first implementation may deliberately omit
`LISTEN/NOTIFY` until measurements show that polling latency or database load justifies it.

Planned behavior:

- command/send creates one logical delivery
- event/publish creates one delivery per logical subscription
- service instances compete inside the same subscription
- claims use `FOR UPDATE SKIP LOCKED`
- leases recover abandoned work
- retries persist across process restarts
- poison/terminal failures remain inspectable
- cleanup and migrations are explicit
- multiple API/worker instances require no distributed cache

Likely storage concepts are messages, subscriptions and deliveries. The exact schema, transaction
boundaries and request/response behavior must be designed before the first migration is written.

PostgreSQL-only implementation details must not escape this package:

- SQL schema and table names
- `LISTEN/NOTIFY` channels or payloads
- polling intervals
- row locks and `FOR UPDATE SKIP LOCKED`
- database leases
- PostgreSQL connection factories and migrations

Other transports may use broker acknowledgements, visibility timeouts, partitions or queues while
preserving the same TinyBus command/event semantics.

## Explicitly deferred

- RabbitMQ and Azure Service Bus implementations; their semantics still inform the common transport
  boundary before it is stabilized
- sagas
- scheduling
- dashboards
- OpenTelemetry
- gradual rollout or routing policy features
- TinyDispatcher integration

These stay outside the first distributed runtime until a concrete product requirement justifies
them.
