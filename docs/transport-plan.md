# Transport and command runtime plan

Status: the first in-memory topology slice is implemented, verified and approved.
The second slice's public reconciliation/loading seams are implemented, verified and approved.
The third slice's reusable ownership validation is implemented, verified and approved.
The subsequent correction keeps CommandRoute passive and moves checks to their owning boundaries;
that correction is approved. The user's TopologyWorker proposal replaces the rejected initializer;
startup readiness and Hosting.Abstractions in TinyBus are approved. The worker slice is implemented,
verified and approved.
Production transport send/receive contracts remain proposals. Native activation is complete
at `ef1180d`.

## Goal

Connect the existing application API to the existing handler execution through a transport.
Start with Orders sending CapturePayment to Payments. Design its complete journey, including
failures, before introducing interfaces. PostgreSQL is the first provider; Azure Service Bus is
the comparison that keeps the common contract independent of database mechanics.

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
  -> Azure Service Bus: queues, topics, subscriptions and routing metadata
```

Commands and requests resolve to their owning logical service; events reach interested services.
Several instances of payments contribute the same logical service, not extra event subscriptions.
Applications do not maintain a second manual command-routing map. No service exchanges full manifests
with another, and no permanent coordinator or global manifest service exists. Each service reconciles
only its own declarations; shared transport infrastructure accumulates the routing facts. Senders
consume only the command routes they need. CLR message types stay local: shared DTO assemblies are
optional, not a requirement. Wire compatibility is defined by contract identity and serialization.

Topology contribution describes intended participation, not whether a process is currently alive.
For this slice reconciliation is additive only: absence from a manifest is never deletion intent.
An older replica must never erase declarations introduced by a newer replica. Repeated contributions
from the same service are idempotent; two distinct command owners conflict. Removal, retirement and
ownership transfer require a later revision policy designed for rolling deployments.

The reconciler consumes declarations, validates the combined topology and brings transport routing
into agreement with it. For PostgreSQL it persists topology in shared tables. For Azure Service Bus
it materializes queues, topics and subscriptions. A shared table store is not a mandatory common
abstraction: each provider owns how the declarations and resulting routes are represented.
Reconciliation runs as an operation in each contributing service, never as a permanent central daemon.
Start with startup reconciliation and startup cache loading. A synchronous send consults its local
immutable snapshot; network access and management calls never occur inside route lookup. Optional
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
2. TinyBus finds its contract identity and owner in the local command route cache, assigns message identity and
   serializes the command into an envelope.
3. The transport accepts the envelope for that destination and confirms acceptance to TinyBus.
4. A Payments instance receives a delivery that it temporarily owns.
5. TinyBus identifies the command contract, deserializes it, creates a delivery scope and invokes
   the existing typed CommandExecutor once.
6. TinyBus determines the processing outcome. The transport records completion or the agreed
   failure disposition. The scope is disposed on every path, including cancellation.

| Responsibility | Owner | Boundary |
| --- | --- | --- |
| Contract identity and typed invocation | Generated TinyBus code | No runtime assembly scanning or invocation delegates |
| Logical ownership/subscriptions | Shared transport infrastructure | Each service adds or confirms its own declarations |
| Command lookup | Local immutable route cache | Synchronous ContractIdentity to ServiceIdentity lookup, no network |
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
| Missing route, unknown outbound contract or serialization failure | Fail before calling the transport |
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

- For SendAsync, the logical destination comes from the local route cache as ServiceIdentity; any
  Destination model must not encode a broker address. Physical mapping stays in the adapter. For PublishAsync,
  propose destination omission and transport fan-out through reconciled subscriptions. This means
  nullable Destination needs clear per-operation validation. Do not also fan out in the caller.
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

## Later work — intent only

After reviewing this proof, agree the next small slice. Production topology reconciliation and
snapshot loading, command preparation/serialization, receive execution and transport implementation
remain separate work. Request ownership is not part of this command/event proof. Event subscriber
facts identify services; per-handler durable outcomes and retry selection remain future design.

Preserve the mandatory PostgreSQL/ASB comparison before approving production transport contracts.
Only real provider tests can establish durable acceptance, concurrent ownership and restart recovery.
No PostgreSQL/ASB implementation or schema is authorized by this in-memory slice.

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
Runtime consumes routing facts through local caches. There is no permanent TinyBus brain.
