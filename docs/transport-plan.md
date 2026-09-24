# Transport and command runtime plan

Status: the first in-memory topology slice is implemented, verified and approved.
The second slice's public reconciliation/loading seams are implemented, verified and approved.
The third slice's reusable ownership validation is implemented, verified and approved.
The subsequent correction keeps CommandRoute passive and moves checks to their owning boundaries;
that correction is approved. The original TopologyWorker proved startup readiness through route
loading; the provider comparison has now replaced it with one common TinyBusRuntime and the semantic
ITransport initialization seam.
Application registration is also implemented, verified and approved. Outbound usage discovery is
explicitly deferred; registration must not require a manually maintained outbound-command list.
Production transport send/receive contracts remain proposals. Native activation is complete
at `ef1180d`.

Latest routing decision: RabbitMQ uses deterministic contract-to-exchange/routing-key mapping and
service queue bindings. Route lookup and caching are PostgreSQL capabilities, not unconditional
runtime requirements. The old ITopologyReconciler and ICommandRouteSource seams are removed;
CommandRoute and CommandRouteCache are internal to TinyBus.PostgreSql. Readable ownership metadata,
if needed, is a private RabbitMQ
mechanism; Management API is not part of the core operational contract. AddTinyBus no longer creates
a route cache or requires a route source. Historical slice descriptions below describe the earlier code.

## Goal

Connect the existing application API to the existing handler execution through a transport.
Start with Orders sending CapturePayment to Payments. Design its complete journey, including
failures, before introducing interfaces. The user selected PostgreSQL and RabbitMQ as the initial
provider pair, developed through the same small slices and real integration tests. The earlier
Azure Service Bus comparison remains a reference; its adapter is not part of this work.

Use Transport as the name. Successful SendAsync means confirmed durable transport acceptance,
not handler completion. This is the agreed direction; retries, identity and delivery ownership
still need the decisions below. A responsibility in this plan does not imply a separate class.

## Existing code and missing connections

We have IBus declarations, MessageEnvelope, generated local/composed topology, scoped handler
registrations and local command/event/request execution. We now have an internal immutable command
route cache, public reconciliation/loading seams and a test-only topology accumulator implementing
those seams. Serialization, production topology reconciliation,
transport operations, a receive worker and durable delivery state remain future work.

The agreed routing direction is service-contributed topology:

```text
source generator -> GeneratedTinyBusManifest
ServiceIdentity + manifest(s) -> ServiceTopology
each service contributes its topology
  -> transport-specific topology reconciliation
  -> PostgreSQL: shared topology tables and routing map
  -> RabbitMQ: exchanges, service queues and bindings
```

Commands reach their owning logical service; events reach interested services. The provider decides
whether routing uses local ownership lookup or broker bindings. Request/reply routing remains later work.
Several instances of payments contribute the same logical service, not extra event subscriptions.
Applications do not maintain a second manual command-routing map. No service exchanges full manifests
with another, and no permanent coordinator or global manifest service exists. Each service reconciles
only its own declarations; shared transport infrastructure accumulates the routing facts. Providers
using a local cache load only needed command routes. RabbitMQ can route by contract without discovering
the owning service in the sender. CLR message types stay local: shared DTO assemblies are
optional, not a requirement. Wire compatibility is defined by contract identity and serialization.

Topology contribution describes intended participation, not whether a process is currently alive.
For this slice reconciliation is additive only: absence from a manifest is never deletion intent.
An older replica must never erase declarations introduced by a newer replica. Repeated contributions
from the same service are idempotent; two distinct command owners conflict. Removal, retirement and
ownership transfer require a later revision policy designed for rolling deployments.

The reconciler consumes declarations, validates the combined topology and brings transport routing
into agreement with it. For PostgreSQL it persists topology in shared tables. For RabbitMQ it declares
exchanges, queues and bindings. A shared table store is not a mandatory common
abstraction: each provider owns how the declarations and resulting routes are represented.
Reconciliation runs as an operation in each contributing service, never as a permanent central daemon.
Start with startup reconciliation and the selected provider's readiness prerequisites. Providers
using a cache load and validate it before readiness; RabbitMQ derives addressing from the contract
and lets the broker apply its bindings. Routing must not add per-send administrative lookups. Optional
periodic refresh comes later, with no generic notification interface or distributed-cache dependency.

For ASB, explicitly define how a sender discovers which queue owns a command contract. Queue creation
alone is not the routing lookup design. Naming or persisted resource metadata may carry that mapping;
choose and validate a mechanism before implementing it. Topology administration credentials and
runtime send/receive credentials must also have explicit owners.

Two remaining gaps must be addressed explicitly:

- ServiceTopology currently contains CLR Type values through MessageDescriptor. Shared topology
  needs portable contract identities and service/consumer identities without loading foreign
  handler assemblies. Agree which metadata is contributed and which stays local.
- Handler-derived metadata does not describe every outbound-only message or reply. Contract
  identity and serialization must also work when the sender has no local handler for that type.

## Command journey and responsibilities

1. Orders calls SendAsync with CapturePayment.
2. TinyBus obtains its contract identity, assigns message identity and serializes it into an envelope.
3. The transport routes by contract and confirms durable acceptance to TinyBus. PostgreSQL may use
   local ownership lookup; RabbitMQ maps the contract deterministically to an exchange/routing key.
4. A Payments instance receives a delivery that it temporarily owns.
5. TinyBus identifies the command contract, deserializes it, creates a delivery scope and invokes
   the existing typed CommandExecutor once.
6. TinyBus determines the processing outcome. The transport records completion or the agreed
   failure disposition. The scope is disposed on every path, including cancellation.

| Responsibility | Owner | Boundary |
| --- | --- | --- |
| Contract identity and typed invocation | Generated TinyBus code | No runtime assembly scanning or invocation delegates |
| Logical ownership/subscriptions | Shared transport infrastructure | Each service adds or confirms its own declarations |
| Command routing | Transport provider | Local ownership lookup where needed; broker-native routing otherwise; no per-send management lookup |
| Reconciliation | Per-service startup operation with transport-specific implementation | No permanent coordinator; no removal from absent declarations |
| Serialization and message metadata | TinyBus runtime | Transport carries the encoded body without interpreting the command |
| Physical destination and transfer | Transport provider | Connection details, queue names and database schema remain provider concerns |
| Scope, invocation and outcome policy | TinyBus receive runtime | Handlers perform business work and do not acknowledge transport deliveries |
| Claim/lock ownership and recording outcomes | Transport provider | Common operations hide row locks, leases and broker lock tokens |

For this first command path, propose one scope per delivery attempt and one handler invocation
within it. Retries create later attempts; executors do not run retry loops. The worker must bound
concurrency and define shutdown behavior rather than starting unlimited tasks.

## Failure contract to agree

| Situation | Proposed behavior |
| --- | --- |
| Unknown outbound contract or serialization failure | Fail before calling the transport |
| Missing local route in a cache-using provider | Fail before physical submission |
| Broker cannot route a command | Surface send failure; do not report successful acceptance |
| Transport explicitly rejects a send | Surface the failure; do not report acceptance |
| Connection loss or cancellation while acceptance is uncertain | Surface uncertainty; do not promise the message was never accepted |
| Unknown inbound contract/version or malformed payload | Invoke no handler; retain the failure through an agreed terminal disposition |
| Dependency activation or handler failure | Do not acknowledge success; apply the agreed retry/terminal policy |
| Handler succeeds but acknowledgement fails | Delivery can recur; business work may already have happened |
| Ownership expires during processing | Reject stale completion; define cancellation/renewal without assuming business work was rolled back |
| Shutdown during a delivery | Stop accepting work, apply the agreed drain/cancellation rule, and leave unfinished delivery recoverable |

Propose at-least-once delivery with explicit duplicate handling, not exactly-once business effects.
Keep the same MessageId across retries of the same prepared message. A new SendAsync call cannot
automatically be assumed to represent the same logical send. Decide how callers preserve identity
after an uncertain outcome. Deduplication must include the recipient where appropriate: one event
may legitimately reach several consumers. Atomic business writes, outgoing messages and completion
need a later transaction/outbox design; a successful handler return does not establish atomicity.

## Proposed transport contracts

The user's draft separates outbound operations, reception and delivery acknowledgement. It is a
design draft, not an instruction to add interfaces to the codebase yet. Destination is a proposed
concept whose representation is still undefined. The receive element below uses ITransportDelivery
consistently with the proposed delivery interface; the original sketch named TransportDelivery.

```csharp
public interface ITransport
{
    ValueTask SendAsync(
        OutgoingMessage message,
        CancellationToken cancellationToken);

    ValueTask PublishAsync(
        OutgoingMessage message,
        CancellationToken cancellationToken);
}

public interface ITransportReceiver
{
    IAsyncEnumerable<ITransportDelivery> ReceiveAsync(
        CancellationToken cancellationToken);
}

public sealed record OutgoingMessage(
    MessageEnvelope Envelope,
    Destination? Destination);

public interface ITransportDelivery
{
    MessageEnvelope Envelope { get; }

    ValueTask CompleteAsync(CancellationToken cancellationToken);

    ValueTask AbandonAsync(
        Exception error,
        CancellationToken cancellationToken);
}
```

The outbound runtime calls ITransport; the receive worker enumerates ITransportReceiver and uses
ITransportDelivery after processing. OutgoingMessage groups the envelope with routing information
without putting transport routing into the domain payload. The interfaces allow different providers
without exposing their SDK objects; their cost is a public lifecycle contract we must maintain.
The simpler alternative is calling a concrete PostgreSQL provider directly, which would couple
the common runtime to the first provider.

Resolve these points before approving the draft:

- The latest RabbitMQ decision supersedes mandatory caller-resolved Destination for SendAsync.
  Propose passing the envelope's contract to the transport and letting it select the route. For PublishAsync,
  propose destination omission and transport fan-out through reconciled subscriptions. Revisit
  whether OutgoingMessage/Destination are needed at all; the sketch above is not an approved
  contract. Do not also fan out in the caller.
- Bind a receiver to its local logical service at construction/configuration; ReceiveAsync need not
  repeat that identity. Define stream disposal, bounded prefetch and ownership of yielded deliveries.
- CompleteAsync means recorded completion. AbandonAsync must have a precise meaning: release for
  retry, or apply a failure policy that may terminate delivery. The current sketch does not state
  how malformed messages or exhausted retries become terminal. Agree that before adding methods.
- Define ownership renewal/loss and cleanup when processing ends without completion. Enumeration
  cancellation and active-delivery cancellation are separate concerns. Do not claim zero allocation
  for the async stream or per-delivery objects without measurements.

## Slice 1: in-memory topology and command routes

Implemented, verified and approved. This slice contains:

- Internal readonly record struct CommandRoute: ContractIdentity plus owning ServiceIdentity.
- Internal CommandRouteCache: a private FrozenDictionary populated from a copied set of routes.
  Its only operation is synchronous TryResolve; it exposes no broker address or mutable dictionary.
- TopologyAccumulator in the test project: simulates accumulated routing facts from ServiceTopology.
  It validates command ownership before applying any part of a contribution, registers commands
  and event subscribers additively, and loads caches filtered to requested contracts.

The test accumulator is a sequential in-memory model, not a production reconciler, service, singleton
or proof of distributed atomicity. It stores no manifest, CLR Type or handler instance. The local
ServiceTopology input still describes local CLR types; those are not copied into routing facts.
Slice 1 added no interfaces, dependencies, generator changes, networking, background workers or storage.

The user approved this rolling-deployment invariant:

```text
payments v1 contributes CapturePayment
payments v2 contributes CapturePayment + RefundPayment
a later v1 contribution leaves RefundPayment intact

absence from manifest != delete
```

The same rule applies to event subscriptions. Retirement and deletion remain explicitly deferred.

Verification: ninety-five Release tests pass, including ten new cases for this slice. The solution
builds with zero warnings/errors. Coverage includes deterministic conflict reporting in both service
registration orders, no partial contribution on conflict, same-service replicas, multiple event
subscribers, versioned lookup, filtered snapshots, snapshot isolation and explicit reload. The
older-replica test preserves both a newer command and event subscription. 10,000 warmed cache lookups
allocate zero bytes on the calling thread; construction/loading allocations are excluded.

## Design conclusions for this slice

| Question | Conclusion |
| --- | --- |
| 1. Core or Runtime? | Routing is a runtime responsibility. Keep it under Routing in the existing TinyBus assembly; no new package for two internal types. |
| 2. Public SPI or internal? | Keep the cache and route internal. Do not introduce ICommandRouteResolver until a concrete consumer requires substitution. A concrete cache already supplies the requested lookup. |
| 3. Synchronous lookup? | Yes: TryResolve performs only local lookup. Reconciliation and cache loading occur outside it. |
| 4. Immutable structure? | FrozenDictionary<ContractIdentity, ServiceIdentity>, copied on construction. Existing snapshots never change when source data or shared facts change. Construction allocates; warmed lookup is measured separately. |
| 5. No owner? | TryResolve returns false. The future outbound caller must fail before sending, without silently performing I/O or routing by namespace. Its exception/API shape is not implemented here. |
| 6. Duplicate ownership? | One route per contract name/version. Same service is idempotent. A different service is rejected with InvalidOperationException naming the contract, version and both owners in ordinal order. Test reconciliation validates before mutation. |
| 7. Rolling deployment? | Add or confirm only. An older replica cannot remove newer command routes or event subscriptions. |
| 8. Stale retirement? | Deferred to an explicit revision/retirement policy. No absence-based deletion, timeout cleanup, ownership reassignment or shutdown deregistration. |
| 9. ASB without a coordinator? | Adapter-managed resource metadata can materialize ownership and load a local snapshot during startup/refresh. Candidate mechanisms and concurrency limits are below; none is selected or implemented. |
| 10. Semantics versus adapters? | TinyBus owns contract/service identities, unique command ownership, multiple event subscribers, additive registration and local lookup. Adapters own persistence, atomic claims, resource naming, management APIs and physical delivery mapping. |

Slice 1 kept reconciliation in synchronous test support. The user subsequently approved the public
adapter seams in slice 2 below: reconciliation/loading are asynchronous provider operations, while
the route cache remains internal and synchronous. CommandRoute becomes public at that boundary.

Startup ordering has a deliberate limit: a cache loaded before an owner registers remains missing
that route until an explicit load. There is no automatic refresh loop or network-on-miss behavior.
The next slice below requires a complete initial load before startup succeeds. A required owner
not yet registered therefore prevents startup; automatic startup retries remain outside this slice.
Existing tests exercise explicit reload without mutating the old snapshot.

## ASB ownership exploration — adapter detail, not a blocker for this proof

Queue UserMetadata and management enumeration can carry/read routing facts outside the send path.
A per-service list is simple, but cannot by itself enforce a unique owner under simultaneous claims
on different service queues. It also has a size limit; it is not an unlimited manifest store.
See [UserMetadata](https://learn.microsoft.com/en-us/dotnet/api/azure.messaging.servicebus.administration.queueproperties.usermetadata)
and [queue enumeration](https://learn.microsoft.com/en-us/dotnet/api/azure.messaging.servicebus.administration.servicebusadministrationclient.getqueuesasync).

An alternative to explore is one deterministic ownership resource per command contract/version,
with the owning service in metadata: create once, read/verify on an existing entity, never overwrite
a different owner. ASB reports an existing-name conflict when creating a queue. This suggests a
provider-local claim mechanism; quotas, naming collisions, metadata validation and concurrent
creation/recovery still require a real adapter prototype. It is a design inference, not a verified
distributed guarantee. See [queue creation](https://learn.microsoft.com/en-us/dotnet/api/azure.messaging.servicebus.administration.servicebusadministrationclient.createqueueasync).

Neither option requires a central TinyBus process or a mandatory external database. Choose the ASB
representation within that adapter; the runtime still consumes ContractIdentity to ServiceIdentity
facts and maps the service to a physical destination inside the transport.

## Slice 2: startup provider boundaries — implemented, verified and approved

Concrete need: per-service startup must contribute its own topology and obtain routing facts for
its local cache. The current test accumulator combines these operations, while independently
packaged transport adapters will supply their production implementations.

The user approved these two public adapter extension points in TinyBus/Abstractions:

```csharp
public interface ITopologyReconciler
{
    ValueTask ReconcileAsync(
        ServiceTopology topology,
        CancellationToken cancellationToken = default);
}

public interface ICommandRouteSource
{
    ValueTask<IReadOnlyCollection<CommandRoute>> LoadAsync(
        IReadOnlyCollection<ContractIdentity> requiredContracts,
        CancellationToken cancellationToken = default);
}
```

ITopologyReconciler writes my service's capabilities; ICommandRouteSource reads accumulated command
ownership. ReconcileAsync adds or confirms only the supplied service's capabilities; it never removes absent
declarations. LoadAsync reads only the requested routing facts and may perform I/O. This is startup
or explicit reload work, never the per-send path. Missing owners are omitted so the cache retains
its existing TryResolve(false) behavior. Loading errors and cancellation propagate rather than
being converted to an empty successful snapshot.

CommandRoute is public because it crosses the adapter boundary; CommandRouteCache stays
internal. No ICommandRouteResolver or new coordinator class is added. ServiceTopology is local
input to its service's adapter, not a manifest transmitted to other services. The adapter extracts
portable facts before writing shared infrastructure.

These are distinct write/read responsibilities: a sender reads foreign routing facts without
owning those declarations. The simpler alternative is calling a concrete provider directly at
startup; that couples startup to one adapter. The cost of these seams is two public contracts plus
a public route record to maintain. No new package is needed.

The existing test accumulator now implements both seams and returns route snapshots. Tests exercise
pending completion, failure and cancellation through a controllable availability task, then verify
real accumulated facts. Cancelled/failed reconciliation does not add capabilities in this test model;
production adapters must separately define partial application under interruption. Additive retry
must remain safe. A returned route snapshot and a constructed cache are unaffected by later additions.

All 103 tests pass in Release, including eight new cases. Existing allocation checks still pass.
The Release solution build has zero warnings/errors and isolated package verification passes.
The tests establish behavior of the in-memory adapter; they do not prove distributed ownership,
atomicity or durability. No production startup wiring, networking, hosted workers, PostgreSQL/ASB
operations or automatic refresh is added. The user approved this slice and continuing with the
reusable ownership rule below.

## Slice 3: reusable command ownership validation — implemented, verified and approved

CommandRoute owns ValidateOwner(ServiceIdentity): the same owner is valid, and a different owner
produces a deterministic conflict naming both services and the contract/version. The model already
contains the values needed by this rule, so a separate validator abstraction is unnecessary.

The test accumulator calls this production rule before mutating its test storage. Cache construction
also calls it to reject conflicting provider facts and coalesce repeated facts for the same owner.
The synchronous lookup path is unchanged. This local validation does not replace atomic ownership
enforcement in PostgreSQL or a broker adapter; checking then writing shared state without concurrency
control would still race.

All 106 tests pass in Release. The three additional cases exercise duplicate same-owner input and
conflicting cache input in both orders. Existing additive reconciliation and allocation checks pass.
Storage remains in the test project; no global in-memory topology is added to production.

## Responsibility correction: route facts and ownership policy — implemented, verified and approved

The user clarified that CommandRoute is a fact, not the owner of distributed-topology policy.
It is again a passive readonly record struct. Reconciliation validates authoritative ownership;
CommandRouteCache validates only that a supplied snapshot has no conflicting route facts. Local
snapshot validation cannot establish ownership in shared infrastructure.

The existing small checks stay at these two boundaries. No public validator abstraction is added
merely to share their implementation. Behavior, deterministic diagnostics and same-owner idempotency
remain unchanged. This supersedes the placement of ValidateOwner on CommandRoute in slice 3 above.
The production reconciliation implementation and its atomic ownership enforcement remain future work.

## Slice 4: per-service topology worker — implemented, verified and approved

The user rejected TopologyInitializer and proposed an internal TopologyWorker : BackgroundService.
The host is its consumer. The worker owns when reconciliation and route loading run within this
service's lifecycle. The provider seams keep their existing write/read responsibilities.
The user approved initial loading as a startup invariant. The worker's StartAsync awaits this
sequence before starting its background phase:

```csharp
await reconciler.ReconcileAsync(serviceTopology, cancellationToken);

var routes = await routeSource.LoadAsync(requiredContracts, cancellationToken);

ValidateRequiredRoutes(routes);

routeCache.Replace(routes, cancellationToken);
```

This outlines the implemented flow; cancellation checks also separate the phases. StartAsync completes
only after successful validation and immutable snapshot publication. Then it calls base.StartAsync
to start ExecuteAsync. Initialization does not run a second time in ExecuteAsync. That method is
reserved for future refresh, using the same component and publication rules.
The [BackgroundService implementation](https://github.com/dotnet/runtime/blob/v9.0.10/src/libraries/Microsoft.Extensions.Hosting.Abstractions/src/BackgroundService.cs)
returns from its default StartAsync when ExecuteAsync yields, so initial loading must be awaited
in the override before delegating to the base lifecycle.

This slice performs initialization once. ExecuteAsync completes immediately. Waiting for change signals or periodically
refreshing remains later work. The worker uses this service's topology and a supplied set of required
outbound contracts; inbound handlers do not identify which commands this service sends.

Replace on the existing internal cache validates supplied facts and builds a complete
FrozenDictionary before publishing the new snapshot with Volatile.Write. Readers use Volatile.Read
to obtain one complete snapshot
and retain synchronous local lookup. Invalid replacement leaves the previous snapshot intact.
CommandRoute stays passive; snapshot consistency checks remain inside the cache.

Provider failure or cancellation must not publish an empty success. Failed reconciliation prevents
loading. If loading fails after reconciliation, the added shared facts remain: there is no rollback
across these operations. Initial provider or validation errors propagate from StartAsync and fail
host startup. Startup cancellation also propagates; no background phase starts on an initial failure.

The user's requirement to load all required routes is interpreted strictly: every required contract must
have an owner before publication. A missing owner fails startup rather than leaving the first Send
to discover incomplete initialization. This adds a startup completeness requirement without changing
ICommandRouteSource: it still omits unknown owners, and TryResolve still returns false for unknown
contracts without network-on-miss. With no required outbound commands, a successfully loaded empty
snapshot is valid. An empty initialized snapshot and an uninitialized cache are distinct states.
Reconciliation remains additive, so an older replica cannot erase newer declarations.

Hosting integration must await this worker's StartAsync before advertising TinyBus readiness.
Any TinyBus send attempted before initialization must be rejected, not served by an empty fallback.
The cache rejects lookup before initialization. IBus has no implementation yet; carry this guarantee
through the send path when the outbound runtime is added.
Hosted consumers that send during startup must respect this ordering, including when host service
startup is configured to run concurrently. Do not claim arbitrary startup consumers are gated by
registration order alone.

The user approved Microsoft.Extensions.Hosting.Abstractions in TinyBus alongside DI abstractions.
Package verification checks both direct dependencies. Tests reference Microsoft.Extensions.Hosting
to exercise a real host. Public host registration and production provider implementations remain
outside this slice; the worker is registered explicitly in the tests.

All 121 tests pass in Release, including fifteen new cases. Real-host tests use the test accumulator
to prove startup stays pending during reconciliation or loading, succeeds after route publication,
and fails on provider errors, conflicts, missing required owners or cancellation. Failed startup
does not signal ApplicationStarted or expose usable cache contents. No required commands permits
a successfully initialized empty snapshot. Cache tests verify rejected replacement preserves prior
state and readers retain the previous snapshot while construction runs on another thread. Existing
additive and zero-allocation warmed lookup checks still pass. Package verification passes with both
consumers and expected diagnostics. These tests do not establish distributed provider guarantees.

The simpler alternative is application-owned startup code calling the two seams. The worker adds
one internal class and hosting integration, giving each host an owned lifecycle for this operation
and future refresh. No additional coordinator or initializer was introduced. The user approved this
slice and the ownership responsibility correction by requesting a commit and continuation.

## Slice 5: application registration — implemented, verified and approved

The user selected services.AddTinyBus(bus => { bus.Service("payments"); }) as the application seam,
replacing the rejected AddTinyBusTopology proposal. Service sets the logical identity. The callback
configures registration; generated capabilities and internal runtime wiring belong behind this API.

The generated entry point chooses the calling assembly's composed manifest and scoped handler
registrations. A public generic core overload configures TinyBusOptions and registers ServiceTopology,
CommandRouteCache and TopologyWorker. The provider seams are resolved through DI when the host starts;
missing providers fail startup. Repeated AddTinyBus registration is rejected before any changes.

The user clarified that outbound requirements must be inferred from IBus usage, complementing
handler-derived inbound topology. No manual RequireCommand API is added. This slice configures the
service and inbound capabilities, supplying no outbound requirements until usage analysis is
implemented. It does not implement IBus sending or claim complete outbound route readiness.

Later generator work will collect semantic IBus usages and compose command requirements from
referenced assemblies. Portable compiler metadata and unresolved generic calls need explicit design.
The generator must distinguish command routing, event publication and request/reply requirements.
Production provider configuration remains separate work.

The user explicitly deferred that discovery work and rejected RequireCommand as a registration
fallback: it duplicates knowledge at call sites and can drift. Any future explicit declaration for
unresolved usage should live near the usage or helper; its contract remains undecided. The current
registration slice is approved by the user's instruction to commit and move on.

## Slice 6: provider selection through options — implemented, verified and approved

The user approved provider selection through options extensions, following TinyFlags. Provider
packages extend TinyBusOptions and register their implementations through Services. A provider
supplies ITopologyReconciler and ICommandRouteSource; core need not know its concrete types or
require both responsibilities to share a class. Missing capabilities still fail host startup.

Options operate on a copy of existing service descriptors so provider TryAdd defaults respect
application overrides. Successful configuration publishes those registrations back to the caller's
collection; callback or validation failures leave its descriptors unchanged. No provider is activated
during registration. DI creates providers when the worker resolves them.

The test-only extension uses the existing topology accumulator. Real-host tests exercise reconciliation,
route reads, pending startup and missing capabilities; registration tests cover overrides and failure
without partial changes. All 135 Release tests pass. Provider projects TinyBus.PostgreSql and
TinyBus.RabbitMq are present in the solution as requested, with no transport implementation yet.
The user approved the registration hook and project scaffolding by requesting a commit and continuation.

## Paired provider topology proof — next design discussion

The first paired slice should implement registration, additive topology reconciliation and routing
preparation against real PostgreSQL and RabbitMQ instances. Apply the same behavioral checks to each:
same-service replica idempotency, conflicting owners including concurrent claims, preservation of
newer declarations after older-replica reconciliation and persistent facts
after recreating the provider. Each application host selects one provider.
Filtered route reads are tested for cache-using providers; RabbitMQ tests contract-derived addressing
and bindings without requiring an ownership lookup in the sender.

Before implementation, agree ownership representation and discovery in each adapter. RabbitMQ
direct exchanges route a matching key to one or more queues, so bindings alone do not enforce
TinyBus's unique command-owner invariant. This is a design inference from
[RabbitMQ exchange routing](https://www.rabbitmq.com/docs/exchanges).
The RabbitMQ adapter must remain usable without PostgreSQL. PostgreSQL transactions and RabbitMQ
management/resources stay private to their respective adapters. Partial reconciliation failure
must be specified honestly; the sequential accumulator's guarantees are not distributed guarantees.

Provider selection hooks are ready, but UsePostgreSql/UseRabbitMq and their configuration contracts
await real provider implementation. The first paired slice's design must pass the common-seam review.

### Provider-specific readiness seam — implemented and verified, awaiting review

The shared startup invariant is:

**TinyBus is not ready until the selected provider has completed the initialization it requires
for safe messaging.**

Route loading and caching are PostgreSQL capabilities, not unconditional TinyBus runtime requirements.
CommandRoute and CommandRouteCache are internal to TinyBus.PostgreSql. ICommandRouteSource and
ITopologyReconciler were removed rather than imposed on RabbitMQ. The previous proposal requiring
Management API route discovery for RabbitMQ is withdrawn.

| Responsibility | PostgreSQL | RabbitMQ |
| --- | --- | --- |
| Reconcile local capabilities | Add or confirm topology in shared tables | Declare or confirm exchanges, queues and bindings |
| Validate command ownership | Enforce a single owner in shared storage, including concurrent claims | Enforce a single owner through a provider-specific check during reconciliation/startup |
| Prepare command routing | Load required routes; validate and publish an immutable local cache | Derive exchange/routing key from contract identity; owning service binds its queue |
| Become ready | Reconciliation and validated cache publication have completed | Broker topology reconciliation and ownership validation have completed |
| Send later | Resolve from the provider's prepared routes | Use native broker routing without reading owner metadata or loading a route cache |

The startup sequences are therefore:

```text
PostgreSQL: reconcile topology -> load required routes -> build validated immutable cache -> ready
RabbitMQ:  reconcile broker topology -> validate command ownership -> ready
```

These sequences describe readiness prerequisites, not permission to install conflicting bindings
before checking ownership. The RabbitMQ implementation must prevent a competing owner from leaving
an active command binding. Exact operation ordering and recovery require a real provider proof.

**Routing != ownership validation.** Deterministic addressing tells the broker where to route;
it does not prevent two services from binding queues for the same command. Ownership validation
must handle simultaneous claims, allow replicas of the same service and reject different owners.
It belongs to reconciliation/startup, independently of how sends find their destination. Cache
consistency checks likewise do not establish authoritative ownership.

If RabbitMQ needs readable ownership metadata, keep it as a small RabbitMQ-specific mechanism.
Its representation and concurrency guarantees remain to be designed and verified. Management API
is not a core operational requirement, and a sender must not need ownership reads to route a command.
Do not introduce a common metadata-store abstraction, require PostgreSQL for RabbitMQ, or disguise missing
ownership enforcement as successful broker declaration.

RabbitMQ command addressing is now a provider-internal compatibility contract. Commands use the fixed
`tinybus.commands` exchange and a human-readable routing key composed from the contract name and
invariant version, such as `payments.capture.v1`. Tests lock the exact mapping and prove culture
independence, version distinction and the 255-byte routing-key bound documented by
[RabbitMQ](https://www.rabbitmq.com/tutorials/tutorial-five-dotnet). ContractIdentity remains in the
envelope; the address neither stores nor validates the command owner. Exchange declaration, bindings,
ownership enforcement and send acceptance remain unimplemented.

#### Runtime refactor — implemented

AddTinyBus previously created CommandRouteCache, used its registration to detect duplicate runtime
registration, and wired a TopologyWorker that always resolved ICommandRouteSource. The refactor made
these changes:

- AddTinyBus registers ServiceTopology and one common TinyBusRuntime. It does not create a route
  cache. The owned ServiceTopology registration also identifies an existing TinyBus runtime.
- TinyBusRuntime owns the host startup boundary and awaits ITransport.InitializeAsync. It receives
  exactly one selected transport and knows no provider substeps.
- Cache-using providers retain required-route validation and immutable publication before readiness.
  RabbitMQ registers no dummy route source or empty cache to satisfy common wiring.
- Initial failure or cancellation propagates and fails startup. Do not signal readiness from an
  eventually initialized background operation. Future refresh remains deferred.
- Preserve staged registration, application overrides and the generated manifest/handler wiring.
  IBus sending remains unimplemented; its eventual entry point must respect provider readiness.

The public ITransport seam currently exposes only initialization with ServiceTopology and cancellation.
Send, publish, receive and settlement are deliberately absent until their vertical slices. Providers
do not register independent primary workers; Core retains one readable runtime lifecycle.

Provider tests follow production ownership. Core tests reference neither provider. Route/cache and
route-based startup proofs live in TinyBus.PostgreSql.Tests. TinyBus.RabbitMq.Tests is present in the
solution and remains empty until RabbitMQ has concrete behavior worth testing.

Behavioral checks prove startup stays pending while provider initialization is pending;
failure/cancellation never signals readiness; missing or multiple transports fail startup; a provider
without a route source/cache starts; and a cache-using provider cannot start before required routes are
validated and published. The route-based and native-routing-shaped transports share TinyBusRuntime.
Real PostgreSQL/RabbitMQ tests must still establish concurrent ownership, additive rolling deployments
and restart recovery.

The later send boundary should accept contract-bearing message data and let the selected provider
route it; core must not require a caller-resolved ServiceIdentity for every command. Its exact
signature remains a proposal. Broker readiness does not prove that every remote command is routable;
the future send implementation must surface unroutable commands rather than claim acceptance.

Provider-specific UsePostgreSql/UseRabbitMq configuration and concrete ownership mechanisms remain
to be reviewed. No production provider code, schema or real-infrastructure experiment was added in
this slice. This correction is why the two providers must be developed in parallel: PostgreSQL's
readable route model must not become a universal core contract.

## Later work — intent only

After reviewing this proof, agree the next small slice. Production topology reconciliation and
provider-specific routing preparation, command preparation/serialization, receive execution and
transport implementation remain separate work. Request ownership is not part of this command/event proof. Event subscriber
facts identify services; per-handler durable outcomes and retry selection remain future design.

Preserve the mandatory PostgreSQL/RabbitMQ comparison before approving production transport contracts.
Only real provider tests can establish durable acceptance, concurrent ownership and restart recovery.
Project scaffolding and registration hooks do not authorize an unreviewed provider schema or behavior.

## Provider comparison — preliminary, not approval of an interface

| Common requirement | Proposed PostgreSQL mapping | Azure Service Bus reference |
| --- | --- | --- |
| Reconcile service topology | Persist declarations and ownership/subscription map in shared tables | Materialize queues/topics/subscriptions; define metadata and command-route lookup |
| Confirm acceptance | Commit durable message/delivery data | Await broker send acceptance |
| Receive with temporary ownership | Short transaction claims work and records a lease | Receive under PeekLock |
| Record successful processing | Persist completion conditional on current ownership | Complete the locked delivery |
| Recover unfinished work | Expired leases make work eligible again | Abandon or lock expiry permits redelivery |
| Retain terminal failure | Persist terminal delivery state and reason | Dead-letter the delivery |

The PostgreSQL column is a design proposal. SKIP LOCKED supports competing access to queue-like
tables; it does not provide a lease protocol by itself. Durability also requires suitable commit
settings. See PostgreSQL's [locking clauses](https://www.postgresql.org/docs/current/sql-select.html#SQL-FOR-UPDATE-SHARE)
and [WAL settings](https://www.postgresql.org/docs/current/runtime-config-wal.html).
The broker mapping follows Microsoft's [transfer, lock and settlement semantics](https://learn.microsoft.com/en-us/azure/service-bus-messaging/message-transfers-locks-settlement).

Before approval, map ownership renewal, stale completion, uncertain send/acknowledgement and retry
limits as well. Broker delivery counts and future TinyBus attempt counts must not silently be
treated as equivalent. No SQL connection, row, transaction, broker client or lock token leaks into
application handler contracts.

## Compatibility checks and decisions still open

- **Events:** preserve independent recipient outcomes. The current EventExecutor invokes all local
  handlers once but does not persist progress. We must choose how deliveries and durable consumer
  identities relate before using it under redelivery; retrying all handlers can repeat successes.
  ASB topics/subscriptions provide fan-out, but do not decide TinyBus's local-handler retry unit.
  See [queues, topics and subscriptions](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-queues-topics-subscriptions).
- **Request/reply:** reserve a reply destination and request-specific correlation in delivery
  metadata. Returning TResponse remains local; durable reply capture and caller waiting remain
  separate responsibilities. ASB exposes ReplyTo and CorrelationId, which support that direction:
  [message properties](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-messages-payloads).
- **Envelope:** review the existing string Payload, encoding/content type and metadata before
  making it the transport contract. Do not silently replace it with another envelope abstraction.
- **Serialization:** choose the initial format and how generated code obtains serialization
  metadata, including outbound-only contracts. Do not assume handler discovery solves this.
- **Context and middleware:** identify where delivery metadata and invocation belong, but agree
  their contracts separately. No speculative continuation delegates or pipeline framework.
- **Retry policy:** agree ownership, attempt limits, terminal failures and recovery. Avoid stacking
  TinyBus retries on provider retries without defining their different purposes.

Reconciliation publishes only local capabilities; shared transport infrastructure accumulates topology.
Providers use local route caches or native broker routing as appropriate. There is no permanent
TinyBus brain.
