# Transport and command runtime plan

Status: the first in-memory topology slice is implemented, verified and approved.
Production transport contracts remain proposals. Native activation is complete at `ef1180d`.

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
registrations and local command/event/request execution. This slice adds an internal immutable command
route cache and a test-only topology accumulator. Serialization, production topology reconciliation,
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

## Current slice: in-memory topology and command routes

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
No interfaces, dependencies, generator changes, networking, background workers or storage are added.

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

ITopologyReconciler is not added in this slice: the only accumulator is test support, and its operation
is synchronous because it performs no I/O. Production reconciliation is expected to require async I/O.
If independently packaged adapters implement a common reconciliation contract, a public SPI may be
justified then; keep it an adapter extension point, not a handler-facing API. Do not settle visibility
by creating a speculative interface now.

Startup ordering has a deliberate limit: a cache loaded before an owner registers remains missing
that route until an explicit load. There is no automatic refresh loop or network-on-miss behavior.
Production readiness/retry policy for startup remains future design. Tests exercise explicit reload
without mutating the old snapshot.

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
