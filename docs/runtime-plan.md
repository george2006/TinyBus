# TinyBus runtime working plan

This document preserves the agreed direction after the core and source-generator bootstrap. It is
a working plan, not authorization to implement all phases at once. Each feature is sliced and
reviewed before code begins.

## Decisions

### TinyBus owns handler execution

TinyDispatcher stays outside the TinyBus consumption path. TinyDispatcher models in-process
request/handler dispatch; it does not own the distributed semantics of commands, events,
subscriptions, delivery attempts or acknowledgements.

TinyBus owns local execution for:

```text
ICommandHandler<TCommand>
IEventHandler<TEvent>
```

The source generator may later emit typed invocation plumbing once the runtime has a concrete need
for it. No runtime reflection or TinyDispatcher adapter is planned for handler execution.

### Request/response separates callers from consumers

Keep IBus.RequestAsync<TRequest, TResponse> as the caller-facing API: send a request and await a
typed reply. The approved developer experience retains IRequestHandler<TRequest, TResponse> for
application code: return a business response and let TinyBus create and send the correlated reply.
The returned value crosses the local handler/runtime boundary; the reply crosses the transport as
a separate message. Business replies and delivery outcomes are separate concerns.

This convenience models one logical response produced when handler processing completes. It does
not require context.Reply or an output property. Deferred and multi-response conversations are
outside this initial contract and would require separately designed messaging capabilities.

The local registration and invocation slice is implemented below. It does not yet provide
correlation, reply routing or reliable request/reply; those runtime semantics remain under discussion.

Request deliberately names a narrower concept than Command and Event: one request with one typed
reply. The local handler shape is approved; context and distributed delivery remain separate design
work. The goal is clearer application code with a compiler-checked response contract.

### Handler context — future work

Handlers will need a context for the current message and delivery. Candidate information includes
message ID, correlation, causation, headers and delivery details. The context should also provide
an injected publishing capability so a handler can publish through TinyBus's outbound path.
The automatic single-response request path does not require a context operation to send its reply.
Additional outbound operations and any future explicit reply capability require separate design.

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
Multi-assembly topology and native handler activation/invocation are implemented, verified and
approved. Distributed topology and local command routing are the current feature.

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
- an event executor attempts every registered handler once and reports each handler's success;
  future delivery mechanics will use individual outcomes to decide retries
- a command or request has exactly one local handler, already enforced by diagnostics
- cancellation remains distinct from processing failure

Handler execution must use no invocation delegates or runtime reflection. Any new invocation type
must have a concrete runtime consumer and be discussed before implementation.

Proposed slices:

1. Generate command and event handler DI registrations, including referenced assemblies. Prove
   resolution and repeated registration without adding execution behavior.
2. Execute a typed command through the caller's scoped service provider, propagating completion,
   failures and cancellation. Measure TinyBus dispatch allocations.
3. Execute all event handlers once and report a result record per handler. Implemented, verified and approved.
4. Register and invoke typed request handlers locally. Implemented, verified and approved. Automatic
   replies over the transport require separately agreed runtime behavior and implementation slices.
5. Extend the host and packaged consumer to prove execution across assembly boundaries. Implemented,
   verified and approved.

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

#### Slice 3: event execution — implemented, verified and approved

Approved simple contract: EventExecutor(IServiceProvider services) exposes:

```csharp
ValueTask<IReadOnlyList<EventHandlerResult>> ExecuteAsync<TEvent>(
    TEvent message,
    CancellationToken cancellationToken = default)
```

It resolves all IEventHandler<TEvent> registrations from the caller's scope and invokes each once,
sequentially in registration order. Each invocation is awaited inside its own try/catch: success
records true, a handler exception records false, and execution continues with the next handler.
Results are ordered EventHandlerResult records with HandlerType and Succeeded. The approved result
type is a readonly record struct. No handlers returns an empty list. Registrations remain unkeyed
and unchanged from slice 1.

Caller cancellation propagates when a handler throws OperationCanceledException while the supplied
token is cancelled; a handler-local cancellation is recorded as failure. The caller owns the scope
and keeps it alive until completion. There are no retries within ExecuteAsync.

Results identify handlers by CLR type for this local execution. Durable subscription identities,
storage, retry selection/scheduling and richer failure details remain future work. Service-provider
resolution currently occurs before the invocation loop; activation failures propagate for the whole
call. If caller cancellation propagates, this API does not return partial results. Address those
mechanics when designing durable delivery. Do not claim progress survives a process failure.

The result list allocates per execution, and awaiting pending handlers can allocate additional
continuation state. This slice favors the agreed simple result contract; zero allocation is not
claimed for event execution. The warmed command-execution allocation guarantee is unchanged.

Responsibility boundary: EventExecutor owns local handler invocation and collection of outcomes.
Future delivery orchestration will own persisted progress, retries, acknowledgements and transport
concerns. A message ID identifies the event; a consumer/subscription identity identifies its recipient.
Design that identity as delivery metadata rather than modifying the domain payload. No durable
consumer identity or delivery coordinator is introduced in this slice.

#### Slice 4: typed request execution — implemented, verified and approved

Generated manifests register IRequestHandler<TRequest, TResponse> with the approved scoped lifetime,
including internal handlers in referenced assemblies. Repeated registration preserves each binding
once. The approved internal RequestExecutor resolves the handler from the supplied IServiceProvider
and returns HandleAsync directly:

```csharp
ValueTask<TResponse> ExecuteAsync<TRequest, TResponse>(
    TRequest request,
    CancellationToken cancellationToken = default)
```

The caller owns the scope through completion. Response values, synchronous/asynchronous failures
and handler cancellation propagate unchanged. This adds no public executor API, invocation delegates,
runtime reflection, retries or reply routing. The application still implements the existing public
IRequestHandler interface; only local invocation machinery is internal.

Verification covers generated local and referenced registrations, scoped isolation, repeated
registration, exact request/token/response forwarding, pending completion, failures and cancellation.
All eighty-five tests pass in Release. Completed and pending dispatch each allocate zero bytes over
10,000 warmed calls on the calling thread. Startup, first activation and handler-owned task and
continuation work are outside that measurement.

#### Slice 5: executable host and packaged consumers — implemented, verified and approved

The existing host sample executes local and referenced command handlers, invokes both event
handlers and displays their individual results, then resolves the request handler and obtains its
typed response. The host owns one validated DI scope for this local demonstration. RequestExecutor
stays internal; the sample invokes the public IRequestHandler contract directly.

The package smoke check runs the same host against the TinyBus package, compares observable handler
output and retains the existing cross-assembly diagnostics and analyzer isolation checks. Its
single-assembly consumer also proves command invocation and scoped isolation.
This slice changes samples and verification only. It introduces no transport or runtime contract.

Verification passed: direct sample execution, Release solution build with zero warnings/errors,
all eighty-five tests and isolated package verification. The user approved this slice, committed as
`ef1180d`. Native activation is complete; the resumed session is designing the transport boundary.

#### Request/response transport design discussion — open

Agreed API direction:

- The caller sends a request through IBus.RequestAsync<TRequest, TResponse>.
- Application code implements IRequestHandler<TRequest, TResponse> and returns ValueTask<TResponse>.
- TinyBus invokes the handler and turns its returned value into a correlated reply message.
- TinyBus completes the caller's wait when that reply arrives. Sending a reply is separate from
  returning the CLR value locally; a successful handler return alone is not proof of reply delivery.

The following behavior is proposed, not yet approved:

- Start with one logical reply per successful request handling. Extra sends/publications use the
  future context, without changing what the returned response means.
- Bound the caller's wait with a timeout. Caller cancellation or timeout stops waiting; it does not
  promise to cancel or roll back consumer work that may already have started.
- Keep the await in the requesting process for the first version. A process restart loses that
  wait even if messages are durable. Durable business workflow recovery is a separate capability.
- Correlate a reply to a specific request message, using transport-neutral delivery metadata.
  A general conversation CorrelationId alone is insufficient when several requests share it.
- Capture the outgoing reply durably before acknowledging successful request processing. Atomicity
  with business writes depends on the eventual transaction/outbox integration and is not assumed.
- Complete a pending wait once. Define late and duplicate reply handling explicitly; do not use a
  late reply as a reason to rerun completed business work.

Still to agree: timeout configuration, terminal handler-failure behavior at the caller, response
contract validation, null response semantics, duplicate request handling and the transaction
boundary. Domain outcomes belong in TResponse; infrastructure failures need a separate policy.

Responsibility boundaries: the application handler performs business work; internal invocation
resolves and calls it; delivery orchestration captures/sends replies and handles recovery; caller
correlation completes the pending wait. RequestExecutor remains internal and owns only local
invocation; the response sender and caller correlation mechanism have not been implemented.

Approval covers the local registration and invocation slice above. Agree the remaining transport
behavior before its implementation, and retain the transport-seam review before PostgreSQL work.

### 3. Transport-independent outbound and inbound boundaries

The current plan and in-memory topology slice live in [transport-plan.md](transport-plan.md).
The user authorized the minimal ownership/cache proof; production transport interfaces remain drafts.
Transport remains the agreed name. Successful sending means confirmed durable transport acceptance,
separate from handler completion. Start with a command journey and challenge gaps in routing,
outbound contract metadata, ownership and failure behavior before introducing interfaces.

Each service combines its generated manifest(s) with ServiceIdentity as local ServiceTopology and
reconciles only its own capabilities into shared transport infrastructure. Services never exchange
full manifests. No central TinyBus coordinator, global manifest service or permanent daemon exists.
The infrastructure accumulates ownership/subscription facts; runtime caches contain only needed
command routes. Applications do not maintain a second manual routing map, and shared DTO assemblies
are optional. Repeated instances represent one logical service.

For the current slice reconciliation is additive: absence from a manifest is never deletion intent.
An older replica cannot erase declarations introduced by a newer one. Retirement, ownership transfer
and deletion require a later revision policy. Startup reconciliation and startup route-cache loading
are the intended initial lifecycle. Lookups are synchronous and local; periodic refresh and
notification mechanisms remain deferred.

PostgreSQL persists topology in shared tables; ASB materializes transport-native resources and
ownership metadata. The common contract does not mandate shared database storage or encode physical
destination names. The ASB representation is an adapter decision, not a prerequisite for the memory
proof. The user's draft transport interfaces and ten design conclusions are recorded in the plan.

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
