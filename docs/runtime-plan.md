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

### TinyEvents is an optional outbox integration

TinyBus core does not depend on TinyEvents. A separate `TinyBus.TinyEvents` package may provide a
thin adapter that reuses the existing TinyEvents outbox implementation.

The adapter must reuse TinyEvents behavior rather than reproduce it:

- transactional outbox writes
- SQL Server and PostgreSQL providers
- Entity Framework Core and ADO.NET integrations
- message claiming and leases
- worker execution
- retries and terminal failure handling
- cleanup
- migrations

The intended flow is:

```text
application transaction
        ↓
TinyBus SendAsync / PublishAsync
        ↓
TinyBus.TinyEvents writes an outbound TinyBus envelope through ITinyEventPublisher
        ↓
existing TinyEvents outbox and worker
        ↓
one bridge IEventConsumer<TinyBusOutboxMessage>
        ↓
TinyBus outbound transport
```

TinyEvents processes one internal wrapper event. It does not dispatch the contained command or
event to TinyBus business handlers. The wrapper preserves TinyBus semantics and contains the
message id, contract name and version, message kind, serialized payload, correlation, causation and
headers required by the outbound transport.

The bridge acknowledges success only after the TinyBus transport accepts the message. A retry can
therefore send the same message id again; the receiving TinyBus transport must treat message ids
idempotently.

Request/response does not naturally fit an asynchronous transactional outbox because the caller is
waiting for a response. The first adapter slice will cover `SendAsync` and `PublishAsync`. We will
decide explicitly whether `RequestAsync` delegates to a live transport or remains unsupported by
the outbox path before implementation.

## Feature order

### 1. Multi-assembly topology composition

Each consuming assembly already generates its local manifest. A root service must compose local and
referenced contributions without runtime assembly scanning.

Planned slices:

1. Emit portable compile-time contribution metadata and a uniquely named generated manifest per
   assembly. Implemented, verified and approved.
2. Read referenced contributions inside source-generator analysis and convert all Roslyn symbols
   to TinyBus-owned models at that boundary. Implemented, verified and approved.
3. Generate one deterministic composed manifest in the root assembly.
4. Report duplicate command/request handlers and conflicting semantics across assemblies.
5. Combine the composed manifest with an explicitly supplied `ServiceIdentity` to create
   `ServiceTopology`.
6. Prove the behavior with a real host plus multiple handler libraries and a packaged consumer.

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

Define how generated descriptors activate and invoke handlers through the host service provider.
TinyBus owns acknowledgement boundaries:

- successful handler completion allows acknowledgement
- handler failure reaches the delivery runtime and causes retry/failure handling
- an event invokes every local event handler represented by the service topology
- a command or request has exactly one local handler, already enforced by diagnostics
- cancellation remains distinct from processing failure

This feature will justify any generated delegate or invocation type it introduces. None is added
before the runtime consumer exists.

### 3. Transport-independent outbound and inbound boundaries

Design the smallest concrete transport seam required by both PostgreSQL and the TinyEvents bridge.
The design must define:

- acceptance of a `MessageEnvelope`
- command, event and request semantics
- acknowledgement and failure boundaries
- idempotency by `MessageId`
- serialization ownership
- correlation and causation propagation

No transport interface is introduced until its PostgreSQL implementation and TinyEvents consumer
are both understood as concrete consumers.

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

### 4. TinyBus.TinyEvents

Evaluate and implement the adapter in small slices:

1. Create the package and the concrete serializable outbound wrapper.
2. Map TinyBus command/event envelopes into `ITinyEventPublisher` without duplicating outbox logic.
3. Add the single bridge `IEventConsumer<TinyBusOutboxMessage>` that forwards to the TinyBus
   outbound transport.
4. Define dependency-injection registration and prevent decoration or forwarding loops.
5. Verify transaction participation with the real TinyEvents provider integration.
6. Verify transport failure, worker retry, stable message ids and eventual success end to end.
7. Verify that TinyEvents migrations, leases, retries and cleanup remain the only implementations
   of those concerns in the adapter path.

Open decisions before slice 1:

- whether the adapter decorates `IBus` or exposes an explicit durable-send entry point
- how `RequestAsync` behaves when the adapter is enabled
- which component serializes the inner TinyBus payload
- whether the wrapper should carry `MessageEnvelope` directly or a versioned adapter contract
- package dependency versions and release cadence between TinyBus and TinyEvents

### 5. TinyBus.PostgreSql

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
