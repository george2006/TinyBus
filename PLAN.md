# TinyBus plan

## Bootstrap feature

Prove the core model before introducing distributed infrastructure:

```text
ordinary .NET message
        +
handler declaration
        ↓
source generator
        ↓
deterministic service manifest
```

### Slice 1: core public API — implemented, verified and approved

Create the approved transport-independent contracts and metadata model in `TinyBus`. Validate
explicit contract names at construction. Do not implement runtime behavior, dependency injection,
source generation, infrastructure, serialization or Tiny Suite integrations.

Verification covers the only custom runtime rule in this slice: explicit contract names reject
null, empty and whitespace input while preserving valid names and the default/overridden version.

Approved by the user's instruction to move on. Release build passed with no warnings and all six
tests passed.

### Slice 2: first source-generated command manifest — implemented, verified and approved

Add the source-generator and generator-test projects. Prove one complete vertical path: discover a
concrete `ICommandHandler<T>`, analyze it into Roslyn-free data, plan deterministic output and emit
an `IBusManifest` implementation that compiles and works at runtime.

This slice supports command handlers and convention-based contract names only. Events, requests,
explicit `[BusContract]` identities and diagnostics remain subsequent slices.

The generated source is compiled into a real consumer assembly and exercised at runtime. The full
solution passes eight tests with no warnings. A source scan confirms Roslyn is confined to the
generator entry point, `Discovery` and `Analysis`; the model and all generation code contain only
TinyBus-owned data.

Review refinement: strengthened the working agreement so compound implementation checks must sit
behind intention-revealing decisions when they would interrupt top-down reading. `HandlerAnalyzer`
now uses one guard clause per rejection reason and names the meaningful decisions
`IsConcreteHandler` and `IsPrimaryDeclaration`.

Approved and committed as `6536907`.

### Slice 3: event and request handler semantics — implemented, verified and approved

Extend the existing pipeline to `IEventHandler<T>` and `IRequestHandler<TRequest, TResponse>`.
Analysis must return every supported handler contract implemented by a concrete class, rather than
assuming one class produces one descriptor. Generated request descriptors include their response
type. No contract attributes or diagnostics are part of this slice.

Three executable generator tests cover event metadata, request response metadata and a single
class implementing both a command and event handler. The full solution passes eleven tests with no
warnings. Roslyn remains confined to the generator entry point, `Discovery` and `Analysis`.

Approved and committed as `f42c2c3`.

### Slice 4: contract identity generation — implemented, verified and approved

Read valid `[BusContract]` declarations during analysis and carry their name and version as plain
model values into generation. Messages without the attribute keep the readable fully-qualified CLR
type name and version one. Manifest ordering includes contract version. Invalid explicit identities
remain the following diagnostics slice.

Four executable generator tests cover the default identity, explicit name, explicit version and
safe C# emission of quoted/control characters in a valid name. The complete solution passes fifteen
tests with no warnings.

Approved and committed as `a81b6ab`.

### Slice 5: generated manifest guarantees — implemented, verified and approved

Lock down the generated manifest behavior before diagnostics: output remains identical when source
file order changes, multiple local handlers for one event are retained, and an assembly with no
handlers receives an empty manifest without runtime registration or assembly scanning. No public
API or generator architecture changes are expected.

All three guarantees passed without production changes, confirming that planning already owns the
ordering and generation needs no runtime discovery path. The full solution passes eighteen tests
with no warnings.

Approved and committed as `5ceaf16`.

### Slice 6: contract identity diagnostics — implemented, verified and approved

Introduced the concrete validation boundary needed by the first two diagnostics. Analysis produces
Roslyn-free candidate data and scalar source locations; validation converts valid candidates into
definitions and rejects blank explicit names or versions below one; diagnostics rebind issues only
at the compiler output boundary. Invalid handlers do not enter generation.

Five focused diagnostic cases cover null, empty and whitespace names plus zero and negative
versions. Each diagnostic points at the invalid attribute expression, and the rejected handler is
absent from generated metadata. The complete solution passes twenty-three tests with no warnings.

Review refinement: `TinyBusSourceGenerator.Initialize` now reads as the phase index of the
generator. Incremental provider mechanics live in small named methods, leaving the entry point as
Analyze, Validate, ExtractValidDefinitions, GenerateManifest, RegisterManifest and
ReportDiagnostics.

Approved and committed as `ae1be71`.

### Slice 7: handler topology diagnostics — implemented, verified and approved

Validate the complete set of locally valid handlers after individual contract validation. Report
duplicate command handlers, duplicate request handlers and one CLR message type used with
conflicting semantics. Multiple handlers for an event remain valid. Generation keeps all valid
descriptors; a topology error fails compilation rather than selecting a handler.

Three focused tests cover duplicate command handlers, duplicate request handlers and conflicting
semantics within one handler class. Diagnostics point at every distinct handler involved and are
ordered deterministically. The existing executable multiple-event-handler test remains green. The
complete solution passes twenty-six tests with no warnings.

Approved and committed as `fa24921`.

### Slice 8: compile-time developer experience samples — implemented, verified and approved

Add the three planned sample assemblies. Contracts contains ordinary records and one stable
`[BusContract]` identity. Payments consumes command, event and request messages; Orders independently
consumes the same event. The samples only prove declaration and generation ergonomics—there is no
runtime transport or simulated communication.

The complete solution builds all three samples with no warnings. Inspected generated sources show
one event descriptor in Orders and command, request-with-response and event descriptors in Payments;
both independently consume the stable `orders.order-placed` contract. No generated topology leaks
between assemblies.

Approved and committed as `a8c1938`.

### Slice 9: packaged developer experience — implemented, awaiting review

The `TinySuite.TinyBus` package now carries `TinyBus.SourceGen.dll` as a compiler-only analyzer and
contains its README. A package smoke test creates a unique local package, inspects its assets and
nuspec, restores an isolated consumer using only one package reference, executes its generated
manifest and proves the packaged diagnostics by compiling a duplicate command handler and observing
`TBUS003`. The core package has no runtime package dependencies and the generator never reaches the
consumer output directory.

### Later slices — intent only

The core/source-generator bootstrap is complete. PostgreSQL and all distributed runtime behavior
remain outside this feature and require a new design and slicing discussion before implementation.

## Next feature: composed topology and distributed runtime

The agreed working plan is persisted in [`docs/runtime-plan.md`](docs/runtime-plan.md). It records
the multi-assembly composition design, native TinyBus handler execution and the PostgreSQL runtime.

Architectural decision: TinyDispatcher will not execute TinyBus handlers. Its in-process dispatch
semantics do not represent distributed command and event delivery. TinyBus will own its command,
event and request execution semantics and generate the required invocation plumbing when that
runtime slice begins.

Approved decision: multi-assembly composition will use generated compile-time contribution metadata
through `BusMessageContributionAttribute`. TinyBus will not use the TinyFlags-style mutable runtime
registry because topology completeness and cross-assembly conflicts must be known at compilation.

Transport boundary decision: develop PostgreSQL and RabbitMQ through the same small slices to test
the common abstractions against two different providers. Each host selects one provider.
PostgreSQL schema, locks, leases, polling and any optional `LISTEN/NOTIFY` wake-up remain private to
`TinyBus.PostgreSql`. Correctness cannot depend on notifications, and the first version may omit
them until measurements justify the optimization. The common seam must also fit a broker-based
transport before it is stabilized.

Mandatory checkpoint: after drafting the common transport interfaces and before starting the first
provider slice, compare those interfaces against PostgreSQL and RabbitMQ.
Provider implementation cannot begin until the seam passes that review and the decision is recorded.

### Multi-assembly topology slice 1: portable contributions — implemented, verified and approved

Emit one generated assembly contribution per local message descriptor and a deterministic,
uniquely named public manifest type for that assembly. Contributions use the approved
`BusMessageContributionAttribute`; users never declare them manually. Preserve the existing local
manifest behavior while root composition and referenced-assembly diagnostics remain later slices.

The attribute carries the owning manifest type, contract identity, message type, handler type,
message kind and optional response type. The public manifest name combines a readable assembly
name with a stable hash, preventing collisions between assembly names that normalize to the same
C# identifier. The existing internal manifest remains as a compatibility facade over the new
public manifest.

Three focused generator tests execute the generated metadata, prove every local contribution
points to the same public assembly manifest and verify collision-resistant names. The complete
solution passes twenty-nine tests with no warnings. The package smoke test also passes against an
isolated consumer. Referenced contribution analysis and root composition remain slice 2 and 3.

### Multi-assembly topology slice 2: referenced contribution analysis — implemented, verified and approved

Read generated contribution attributes from referenced assemblies inside the Analysis phase and
convert their Roslyn symbols immediately into `ReferencedMessageContribution`, a TinyBus-owned
model containing only strings, numbers and TinyBus message semantics. The analyzer ignores ordinary
references and returns contributions in deterministic assembly and contract order.

Two focused tests compile real producer assemblies through the TinyBus generator, reference their
emitted binaries from a root compilation and verify complete request metadata, the Roslyn-free model
boundary and deterministic ordering across assemblies. Root composition and cross-assembly
diagnostics remain later slices. The complete solution passes thirty-one tests with no warnings.

### Multi-assembly topology slice 3: composed root manifest — implemented, verified and approved

Connect referenced contribution analysis to generation. The existing internal
`GeneratedTinyBusManifest` now composes the local public manifest and each distinct referenced
public manifest identified by `BusMessageContributionAttribute.ManifestType`. Composition keeps
local messages first, followed by referenced manifests in ordinal assembly/type-name order; each
manifest retains its deterministic local message order.

Public assembly manifests and emitted contribution attributes remain local to their owning
assembly. This prevents duplicate inclusion when a root references both a library and that
library's dependency. Composition uses only the references available to the root compilation;
it performs no runtime assembly scanning and emits no direct references to foreign handler,
message or response types.

Four executable tests compile real producer binaries and cover local plus multiple referenced
manifests, internal types and complete request metadata, an empty root and empty library,
determinism under reference/source reordering, and shared event handlers across libraries with
overlapping dependencies. The Release solution build passes with zero warnings and errors; all
thirty-five tests pass. The existing isolated package smoke test also passes, including the
expected `TBUS003` diagnostic. Packaged multi-assembly host coverage remains slice 6.

Approved by the user's instruction to continue to the next slice.

### Multi-assembly topology slice 4: cross-assembly diagnostics — implemented, verified and approved

Extend `TopologyValidator` with referenced contribution metadata. `TBUS003`, `TBUS004` and
`TBUS005` now reject duplicate command/request handlers and conflicting message semantics across
local and referenced assemblies. Local participants retain source diagnostics; each conflict
involving references also produces a compilation-level diagnostic listing the participating
assemblies and handlers in stable order. Overlapping local and cross-assembly checks do not repeat
the same source diagnostic. Multiple event handlers remain valid, and generation retains every
descriptor rather than silently choosing a handler.

Analysis now records a plain CLR type identity including assembly identity, generic arguments and
array element identity. This distinguishes homonymous messages in different assemblies, while
handler identity includes its owning assembly so homonymous handlers are not collapsed. Roslyn
symbols remain confined to analysis. These rules preserve CLR-message semantics; collisions of
contract names/versions assigned to distinct CLR types are not validated by this slice.

Twelve new diagnostic/executable cases cover conflicts between libraries and local/reference
conflicts, source locations and assembly details, stable ordering across multiple conflicts,
overlapping diagnostics, shared events, and homonymous message/handler types. An array case
uncovered an existing assumption that every message has a declaration location; analysis now
falls back to the local handler when the message has no source location.

The Release solution build passes with zero warnings and errors, all forty-seven tests pass, and
the existing isolated package smoke test passes, including its expected `TBUS003` failure.

Review refinement: renamed `ManifestValidator` to `TopologyValidator` and the generator step to
`ValidateTopology` to name the relationship checks explicitly. The generator remains the
coordinator of per-handler validation and topology validation.

Approved by the user's instruction to commit and continue.

Slices 3 and 4, including the validator rename, committed as `f737d76`.

### Multi-assembly topology slice 5: service topology creation — implemented, verified and approved

The generated root `GeneratedTinyBusManifest` now exposes
`CreateTopology(ServiceIdentity service)`. It creates the existing `ServiceTopology` with the
supplied identity and the complete composed message list. Identity remains an application-owned
value: it is neither inferred from the assembly nor stored in the manifest. No factory abstraction,
interface change, identity-validation policy or runtime activation is introduced.

Three executable generator cases cover local plus referenced handlers, a root with only referenced
handlers, and an empty topology. They also verify complete internal request metadata and two
independent service identities created from the same manifest. The README shows the root call.
The Release solution build passes with zero warnings and errors; all fifty tests pass, and the
existing isolated package smoke test passes, including its expected `TBUS003` failure. Packaged
multi-assembly topology execution remains slice 6.

Approved by the user's instruction to commit and continue.

Slice 5 committed as `7780c36`.

### Multi-assembly topology slice 6: host and packaged composition — implemented, verified and approved

`TinyBus.Sample.Host` creates topology with an explicit service identity, one local command and
the Orders/Payments libraries. Its five descriptors include shared-event handlers and complete
request/response metadata, with internal library handlers and transitively referenced contracts.

The package smoke test builds an isolated copy of the same four projects using the freshly packed
NuGet package. It checks ordered host output, compiler-only generator placement, and `TBUS003`,
`TBUS004` and `TBUS005` failures with referenced-assembly details.

The requested generator refinement keeps both validators inside `Validate` and reports all issues
before calling `Generate`. Errors now suppress generation; warnings alone do not. Handler analysis
uses the incremental syntax transform; `Analyze` combines those results with referenced metadata.
The coordinator retains separate analysis, validation and generation steps. Collaborators are
constructed before use, and contribution argument positions have explicit names.

Diagnostic tests require zero generated sources on errors and cover simultaneous contract and
topology errors. After cleanup, the Release build passes with zero warnings/errors, all fifty-one
tests pass, the sample host runs, and package verification passes its positive and negative cases.

Approved model refinements: `ContractAnalysis` groups the declared
name, version and their diagnostic locations. `MessageTypeAnalysis` groups the CLR message's display
name, generated type name and assembly-aware identity. `MessageHandlerAnalysis` now takes six
arguments, including both concepts. Invalid contract values and value equality are preserved.
The Release build and all fifty-one tests pass after these refactors.

Slice 6 and the generator refinements were approved by the user's instruction to commit and move on.

### Native activation slice 1: command and event registration — implemented, verified and approved

Public assembly manifests expose static `RegisterLocalHandlers(IServiceCollection services)`;
the root's static `RegisterHandlers` composes local and distinct referenced registrations.
Commands and events use scoped service/implementation descriptors. Repeated registration preserves
each distinct pair once, including every event handler. Internal library handlers stay registered
inside their owning assemblies.

The approved DI abstractions dependency lives in TinyBus core. Five executable tests cover scoped
resolution, dependencies and disposal, multiple event handlers, repeated registration, overlapping
assembly references, internal handlers, empty assemblies and deferred request registration.
Package verification now checks that dependency and exercises generated registration.

Emission follows the suite's small concrete writer pattern: `SourceWriter` owns indentation and
line output; `ManifestEmitter` owns contributions, local metadata and root composition. Generated
statements keep construction separate from use. Contract literal coverage includes backslashes,
quotes, control characters and Unicode line separators.

Verification: Release solution build passes with zero warnings/errors, all fifty-six tests pass,
and isolated package verification passes its consumer, composed topology and diagnostic cases.
Inspected the emitted Payments and Host source for readable indentation and named construction.
Approved by the user's instruction to commit and move on.

Execution follows in a separate slice. Handler context with publishing capability and a small
middleware pipeline are recorded as future intent in
[`docs/runtime-plan.md`](docs/runtime-plan.md); their APIs and mechanics remain deferred.

Slice 1 committed as `b09d27a`.

### Native activation slice 2: typed command execution — implemented, verified and approved

The approved concrete `CommandExecutor` resolves `ICommandHandler<TCommand>` from the caller's
scoped provider and returns `HandleAsync` directly. It preserves the command, cancellation token,
completion and failures. The caller owns the scope and keeps it alive until execution completes.
No generator changes, invocation delegates, reflection or extra async wrapper are introduced.

Ten new test cases cover scoped invocation, synchronous and pending completion, synchronous and
asynchronous failures, cancellation before/during execution, missing registration, allocations and
an executable generated-registration scenario with an internal handler and scoped dependency.
The Release solution build passes with zero warnings/errors and all sixty-six tests pass.
Both allocation cases measure zero bytes over 10,000 warmed
calls, including typed struct commands. The pending case reuses a handler-owned task to isolate
dispatch; scope creation, first activation and handler/task allocations are outside measurement.

Approved by the user's instruction to commit and move on. Event execution is the next slice;
ordering and failure behavior require agreement before implementation.

Slice 2 committed as `26d9b5f`.

### Native activation slice 3: event execution — implemented, verified and approved

EventExecutor resolves all scoped event handlers and attempts each once, sequentially in registration
order. It returns an ordered list of EventHandlerResult records containing HandlerType and Succeeded:
true for successful completion, false for a handler exception. Failures do not prevent later handlers
from running. Caller cancellation propagates. There are no retries or keyed registrations.

Nine new cases cover ordered invocation, scoped isolation, message/token forwarding, asynchronous
sequencing, synchronous/asynchronous failures followed by successful handlers, caller cancellation,
handler-local cancellation, empty results and generated local/internal referenced handlers.
All seventy-five tests pass in Release. Retry decisions, durable outcome storage and isolated
activation failures remain later mechanics. The result list allocates per execution; the approved
readonly record struct avoids a separate heap object for each result.

Final verification after the record change: Release solution build passes with zero warnings/errors,
all seventy-five tests pass and isolated package verification passes. A warmed local measurement
of 10,000 executions with two synchronously completing handlers allocated 1,520,000 bytes on the
calling thread; pending completion adds continuation costs. Event execution has no zero-allocation
claim in this slice.

Approved folder refactor: public interfaces live in Abstractions; executors in Execution; envelope,
contract identity and BusContractAttribute in Messaging; service topology and contribution metadata
in Topology. Public namespaces remain TinyBus. All fourteen moved files retain their original contents.
The result record and completed slice were approved by the user's instruction to commit and move on.

Slice 3 and the folder refactor committed as `fddcbf9`.

### Native activation slice 4: typed request execution — implemented, verified and approved

Approved developer experience: callers use IBus.RequestAsync<TRequest, TResponse>; application
developers implement IRequestHandler<TRequest, TResponse> and return the business response.
TinyBus owns converting that value into a correlated reply message. A reply call or output property
is not required for this single-response path. A business reply remains separate from delivery
success or failure.

The approved local slice adds generated scoped request-handler registrations and an internal
RequestExecutor. It resolves the typed handler and returns its ValueTask<TResponse> directly,
preserving the response, completion, failures and cancellation. No additional public executor API,
invocation delegates or async wrapper are introduced. Referenced internal handlers register through
their owning manifests, and repeated registration remains idempotent.

Request names the narrow promise of one typed reply alongside Command and Event. This local
handler shape is approved; context and distributed delivery still require their own design.
The existing payment-status sample shows the application shape without a reply call or output bag.

All eighty-five tests pass in Release. Coverage includes scoped registration and invocation,
referenced internal handlers, pending completion, failures, cancellation and missing registration.
Allocation tests measure zero bytes over 10,000 warmed dispatches for completed and pending results;
scope creation, first activation and handler-owned task/continuation work are excluded.
The Release solution build passes with zero warnings/errors. Inspected generated sample code
registers the closed request/response interface, and isolated package verification passes for
single-assembly and multi-assembly consumers, including the existing diagnostic checks.

The design discussion in docs/runtime-plan.md retains open transport semantics: the lifetime of the
caller's wait, timeout/cancellation, terminal failures, duplicate/late messages and durable reply
capture. IBus.RequestAsync, correlated reply sending and response reception remain unimplemented.
Agree those behaviors and their slices before implementing distributed request/reply.

Approved by the user's instruction to commit and move on. The next slice proves local execution
in the host sample and isolated packaged consumers across assembly boundaries.

Slice 4 committed as `e4b5642`.

### Native activation slice 5: executable host and packaged consumers — implemented, verified and approved

The existing topology host now registers and executes its local command plus referenced payment
command and event handlers through a validated scope. It displays both individual event outcomes and
the typed response returned by the referenced request handler. Request invocation uses the public
handler interface directly; the internal executor stays internal.

Package verification checks the host's complete output, including invocation order, one call to each
event handler and request/response identity. The single-assembly package consumer asserts actual
command invocation and scoped isolation. Existing diagnostics and analyzer-isolation checks remain.
No runtime or generator changes, new abstractions, transport or persisted sample state.

Verification passed: direct sample execution, Release solution build with zero warnings/errors,
all eighty-five tests, and isolated package verification for both consumers. The packaged host
prints both successful event outcomes and the expected payment ID/status. The negative builds
still report TBUS003, TBUS004 and TBUS005 as expected. Approved by the user's instruction to commit
and wait for the next session.

Slice 5 committed as `ef1180d`. Native activation is complete.

### Current feature: distributed topology and local command routing

The resumed session keeps Transport as the name and durable acceptance as the meaning of a
successful send, separate from handler completion. The user requested a concrete plan and asked
that unclear assumptions be challenged.

The plan is in [docs/transport-plan.md](docs/transport-plan.md). Each service reconciles only its own
capabilities from ServiceIdentity plus generated manifests. Shared transport infrastructure accumulates
routing facts. Services do not exchange full manifests and no permanent central coordinator exists.
Senders use a local immutable cache of only the routing facts they need. The hot path is synchronous
and never performs network or management-plane lookups. Shared DTO assemblies are not mandatory.

Reconciliation is additive only: absence from a manifest is not deletion intent. An older replica
must never erase topology introduced by a newer replica. Retirement requires a later revision policy.
PostgreSQL and ASB own their different infrastructure representations behind the common semantics.

### Topology slice 1: in-memory ownership and route cache — implemented, verified and approved

The user's pasted brief authorized a minimal command ownership model, test accumulator and immutable
route cache, with no transport implementation. CommandRoute and CommandRouteCache are internal in
TinyBus/Routing. TryResolve uses a private FrozenDictionary and returns false for an absent owner.
No resolver interface, new package or public SPI is needed for this proof.

The test-only TopologyAccumulator stores command ownership and event subscription facts, validates
conflicts before changing state and loads only requested command routes. Repeated service replicas
are idempotent; different command owners fail with a deterministic diagnostic. Commands and event
subscriptions are never removed because an older replica omits them. Existing cache snapshots remain
unchanged until the caller explicitly loads a new one.

The plan documents all ten design questions from the brief, including visibility, cache misses,
rolling deployment, retirement and ASB ownership options. Production adapters, distributed concurrency,
request ownership, network I/O, persistence, refresh loops and workers remain outside this slice.

Verification: all ninety-five tests pass in Release (ten new cases). The solution builds with zero
warnings/errors. Tests cover ownership conflicts in both service registration orders, no partial
registration on conflict, replica idempotency, event fan-out, versioned lookup, filtered immutable
snapshots and explicit cache reload. The rolling-deployment test preserves both a newer command
and a newer event subscription after an older replica reconciles. 10,000 warmed route lookups
allocate zero bytes on the calling thread. Package verification was not rerun: packaging, public
APIs and generator output are unchanged. Approved by the user's instruction to commit and move on.

Topology slice 1 committed as `f68db83`.

### Topology slice 2: startup provider boundaries — implemented, verified and approved

The user approved both public provider seams. ITopologyReconciler adds or confirms one service's
capabilities asynchronously; ICommandRouteSource loads a snapshot of requested command routes.
CommandRoute is now public because it crosses the adapter boundary. CommandRouteCache remains
internal and synchronous. There is no new coordinator, resolver interface or production provider.

The existing test-only accumulator implements both contracts. It returns routing facts rather than
constructing the runtime cache. A controllable availability task exercises pending completion,
failure and cancellation without replacing the accumulated topology with mock responses.
The existing additive, conflict, filtering and snapshot tests now use the asynchronous operations.
Absence never means deletion. A failed or cancelled load does not become an empty successful result.

All 103 tests pass in Release, including eight new cases. This verifies the in-memory contract proof,
not distributed atomicity or durability. Production reconciliation and route-cache startup wiring
remain later work. The user approved the slice and continuing with shared ownership validation.
The user subsequently requested committing this slice together with the ownership-rule extraction.
The Release solution build passes with zero warnings/errors, and isolated package verification
passes for both consumers, including the existing cross-assembly diagnostic checks.

### Topology slice 3: reusable command ownership validation — implemented, verified and approved

Move the command-ownership rule from test support onto the existing CommandRoute model through
ValidateOwner(ServiceIdentity). Same-owner claims are accepted; different owners produce the same
deterministic error naming the contract/version and both services. No new validator class or global
topology object is introduced.

CommandRouteCache now applies that rule when building a snapshot: repeated same-owner facts collapse
to one route, while conflicts fail before a cache is exposed. The test accumulator uses the same
rule before registering any contribution. Its in-memory storage remains test support, and adapters
remain responsible for atomically enforcing ownership in shared infrastructure.

The user clarified the seam responsibilities: ITopologyReconciler writes my service's capabilities;
ICommandRouteSource reads accumulated command ownership. Both interface comments reflect this.

All 106 tests pass in Release. Three new cache cases cover repeated owners and conflicts in both
input orders. Existing reconciliation, additive deployment, cancellation and allocation checks pass.
The Release solution build has zero warnings/errors. This focused extraction changes no packaging or
generator behavior; package verification last passed with slice 2. Approved by the user's instruction
to commit and move on, together with the provider seams from slice 2.

Topology slices 2 and 3 committed as `792a035`.

### Ownership responsibility correction — implemented, verified and approved

The user clarified that CommandRoute represents a fact, not distributed-topology policy. Restore
it to a passive record. Reconciliation checks command ownership before registration; cache construction
checks only consistency of its supplied snapshot. Both preserve same-owner idempotency, conflict
diagnostics and execution order. The cache does not establish authoritative ownership in shared storage.

The small local checks remain at their respective boundaries rather than introducing another public
policy abstraction to share them. The production provider still needs atomic enforcement when it is
implemented; the accumulator remains test-only. The initializer proposal was rejected and withdrawn.
Verification: all 106 tests pass in Release, including existing ownership, snapshot and allocation
checks. Approved together with the topology worker by the user's instruction to commit and move on.

### Topology slice 4: per-service topology worker — implemented, verified and approved

The user's internal TopologyWorker derives from BackgroundService. Its flow is explicit:
reconcile this service's topology, load required command routes, then replace the local cache snapshot.
The worker owns the hosted lifecycle; the existing provider seams own reconciliation and route reads.
TopologyInitializer was not approved and will not be introduced.

CommandRouteCache.Replace validates and builds a complete immutable snapshot before
atomically publishing it. Readers retain synchronous local lookup; a failed replacement preserves
the previous snapshot. First reconcile/load is the scope; refresh signals and periodic loops stay later.

The user approved initial topology loading as a startup invariant: reconcile, load all required
command routes, validate, publish the immutable snapshot, then consider TinyBus started. Initial
failure or cancellation propagates from startup. Eventual initialization in ExecuteAsync is insufficient.
This sequence runs inside TopologyWorker.StartAsync, before starting the background phase; future
refresh belongs to the same worker. No additional initializer or readiness abstraction is proposed.

Required routes are interpreted strictly: missing owners prevent startup; no required outbound commands
allows a successfully initialized empty snapshot. The provider can still omit unknown owners and
the cache still returns false for unknown contracts. Completeness is checked at startup before publication.

The user approved Microsoft.Extensions.Hosting.Abstractions in TinyBus. Package verification now
checks the two direct dependencies, DI and Hosting abstractions. The full hosting implementation is
used only by tests. Required outbound contracts are supplied explicitly, not inferred from handlers.
The worker remains internal; public host registration and production adapters are not part of this slice.
ExecuteAsync completes immediately for now; no refresh loop or artificial wait is introduced.

Verification: all 121 Release tests pass, including fifteen new cases covering real-host readiness,
provider failures, cancellation, missing ownership, an initialized empty snapshot, replacement
failure/cancellation and concurrent lookup during snapshot construction. Warmed lookups still
allocate zero bytes on the calling thread. Package verification passes both consumers and the
expected diagnostic cases with the new dependency contract. Approved by the user's instruction to
commit and move on.

Topology slice 4 and the ownership responsibility correction committed as `bc33ebc`.

### Topology slice 5: application registration — implemented, verified and approved

The user rejected AddTinyBusTopology(topology, requiredContracts). Applications configure TinyBus
through the following entry point:

```csharp
services.AddTinyBus(bus =>
{
    bus.Service("payments");
});
```

Service identifies the logical service. The configuration callback runs during registration.
TinyBus owns composition of generated capabilities, handler registration, route cache and topology
worker behind this entry point. Applications should not assemble those runtime internals themselves.

The generator emits an internal extension in each consuming assembly that selects its composed root
manifest and registers its handlers. The public core AddTinyBus<TManifest> overload configures
TinyBusOptions, validates the service identity and registers topology, cache and worker. It is the
bridge used by generated code; applications use the non-generic generated entry point above.
No runtime assembly scanning, module initializer or global manifest registry is introduced.

One runtime is allowed per service collection. Repeated AddTinyBus calls fail before changing the
original registration. Invalid or missing service identity also leaves registrations unchanged.
The worker resolves the two provider seams through DI at host startup; missing implementations fail
startup. The DI factory is used for worker activation only, not for message invocation.

The user clarified the next generator direction: inbound topology comes from handler interfaces;
outbound requirements come from IBus usage. There is no RequireCommand configuration API. This
registration slice supplies no outbound requirements while that analysis is pending, and does not
implement IBus sending. It must not be described as complete route readiness for outbound messages.

The sample and packaged consumer use the requested AddTinyBus syntax. Tests cover real-host startup
gating, both missing providers, invalid service configuration, repeated registration, scoped local
handlers and a root with referenced internal handlers. All 131 Release tests pass (ten new cases).
The full solution builds with zero warnings/errors, and package verification passes both consumers
and expected diagnostics. Approved by the user's instruction to commit and move on.

Topology slice 5 committed as `6d7478c`.

### Topology slice 6: provider selection through options — implemented, verified and approved

The user approved provider selection through options extensions, such as bus.UsePostgreSql(...).
Following TinyFlags, TinyBusOptions exposes Services so a provider package can register its own
ITopologyReconciler and ICommandRouteSource implementations. Core has no provider-specific switch
and does not require both capabilities to be implemented by the same class.

Configuration uses a copy of the existing service descriptors. Provider defaults can therefore
respect existing registrations through TryAdd. The completed registrations are applied only after
configuration succeeds; a callback failure or invalid service identity leaves the original collection
unchanged. This protects registration state, not arbitrary side effects inside a callback.

The test transport extension registers the existing real topology accumulator through DI. A real
host proves that the selected provider reconciles its command and exposes ownership through the
route-source interface. Tests also cover delayed startup, existing overrides and failed configuration.
All 135 Release tests pass. The full solution build and package verification pass, including both
packaged consumers and expected diagnostic failures. Production UsePostgreSql/UseRabbitMq extensions
still need their adapters. Approved by the user's instruction to commit and move on.

### Provider project bootstrap — implemented, verified and approved

The user requested TinyBus.PostgreSql and TinyBus.RabbitMq projects in the solution. Both target
net8.0, reference TinyBus, and have matching TinySuite package identities. They contain no placeholder
classes, database migrations, client dependencies or provider behavior. Both build with the solution,
with zero warnings/errors. Approved together with provider registration. Project creation does not
resolve the provider-seam review below.

Provider registration and project scaffolding committed as `387c07c`.

### Provider-specific startup seam — implemented, verified and approved

CommandRouteCache and ICommandRouteSource are provider capabilities, not unconditional runtime
requirements. AddTinyBus no longer creates a cache or requires a route source. The internal
TinyBusRuntime awaits initialization required by the selected provider before readiness.

**TinyBus is not ready until the selected provider has completed the initialization it requires
for safe messaging.**

- PostgreSQL: reconcile topology, load required command routes, build and publish a validated
  immutable route cache, then ready.
- RabbitMQ: declare/reconcile broker topology, validate command ownership, then ready.

The approved public ITransport seam currently exposes only InitializeAsync(ServiceTopology,
CancellationToken). Send, publish, receive and settlement remain later vertical slices. Core owns one
internal TinyBusRuntime hosted service; providers do not register competing runtime workers. A host
must resolve exactly one transport. Missing or multiple transports fail startup before initialization.

Initial failure/cancellation fails startup. Staged registration and duplicate AddTinyBus rejection no
longer depend on cache presence. Tests run the same runtime with a route-based transport that owns its
cache and a native-routing-shaped transport with no route source or cache. Provider-specific routing
types have moved out of Core: ITopologyReconciler and ICommandRouteSource are removed, while
CommandRoute and CommandRouteCache are internal to TinyBus.PostgreSql. Provider tests now live in
TinyBus.PostgreSql.Tests and TinyBus.RabbitMq.Tests; Core tests reference neither provider. The RabbitMQ
project contains no placeholder tests before provider behavior exists. Release build and all 136 tests
pass. Package verification passes
for both packaged consumers and the expected diagnostic cases.

### Current: paired provider topology proof — in progress

Take one agreed behavior through PostgreSQL and RabbitMQ before adding the next. Start with provider
registration, additive topology reconciliation and provider-appropriate routing. Exercise the same invariants
against real infrastructure: replicas are idempotent, different command owners are rejected, older
replicas preserve newer declarations, and topology survives provider recreation.

Before implementation, agree each provider's ownership and routing representation, including
concurrent claims. RabbitMQ bindings alone do not establish unique command ownership. Keep PostgreSQL
rows and RabbitMQ resources private to their adapters; RabbitMQ must not require PostgreSQL to operate.
Do not expand this proof into send/receive, retries or an outbox yet. The shared contract review and
approval remain required before implementing either provider.

The user corrected the RabbitMQ design: derive the exchange/routing key deterministically from the
command contract and let the owning service bind its queue. Do not require a command route cache
or Management API lookup for that path. If readable ownership metadata is necessary, keep its
mechanism small and RabbitMQ-specific; Management API is not part of the core operational contract.

The previous proposal requiring both providers to implement ICommandRouteSource is withdrawn.
PostgreSQL can retain local ownership lookup where needed. The common send boundary should let
the provider route from the envelope's contract, rather than requiring a service destination from
core. That signature and the exact startup refactor remain to be reviewed before code changes.

Broker topology readiness does not prove every remote command has an owner; unroutable sends need
explicit failure handling.

**Routing != ownership validation.** Unique command ownership remains required. Bindings alone do
not enforce it, so separately design and prove the RabbitMQ conflict check without making ownership
reads a prerequisite for routing.
The revised comparison is in docs/transport-plan.md. RabbitMQ command ownership now has the real
topology-journal proof below. PostgreSQL persistence and provider routing preparation remain.

### RabbitMQ command addressing — implemented and verified

TinyBus.RabbitMq now owns an internal CommandAddress that maps a ContractIdentity to physical RabbitMQ
routing. Commands use the fixed `tinybus.commands` exchange and a human-readable routing key composed
from the contract name and invariant version, such as `payments.capture.v1`. Addresses exceeding
RabbitMQ's 255-byte routing-key limit are rejected with a clear error.

The exact mapping is locked by a compatibility test. Additional tests prove name/version distinction,
culture independence, boundary handling and invalid-identity rejection. Addressing remains routing
mechanics and does not establish ownership. No RabbitMQ client, broker declaration, ownership metadata
or Core API was added. All 145 tests pass.

### RabbitMQ topology journal proof — implemented, verified and approved

TinyBus.RabbitMq now has a concrete RabbitMqTopologyJournal backed by one durable `tinybus.topology`
RabbitMQ stream. A versioned provider-owned declaration records one service and all command contracts
it owns. Replaying declarations in broker order produces a deterministic topology: the first accepted
owner wins, replicas of that service remain idempotent, later declarations add facts, and absence
never removes them. A declaration containing any conflicting command is rejected as a whole, so none
of its otherwise-unowned commands leak into the accepted topology.

The journal uses publisher confirms, consumes from the first stream offset and waits until its own
declaration has been reduced before returning. A conflict therefore fails provider initialization
before command bindings can be installed. The durable JSON shape is owned and versioned by the
RabbitMQ package rather than inheriting Core model property names.

Real RabbitMQ 4.2 Testcontainers checks prove rolling-replica preservation, whole-declaration
rejection, deterministic concurrent ownership, reconstruction through a new connection and
ownership survival across a broker restart. This proof does not yet register ITransport, declare
service queues or command bindings. It appends one declaration per reconciliation; avoiding
unbounded journal growth requires an agreed snapshot or compaction policy before production use.
All 149 tests pass.

### RabbitMQ transport initialization — implemented, verified and approved

The internal RabbitMqTransport now prepares one service for safe command messaging. Initialization
first reconciles the complete ServiceTopology through the ownership journal. Only an accepted
topology may declare the durable direct `tinybus.commands` exchange, the durable quorum
`tinybus.{service}` queue and one binding per distinct owned command. Event and request descriptors
do not create command bindings.

The physical topology remains additive. A still-running older replica can confirm its declarations
without removing a command binding introduced by a newer replica. A competing command owner fails
before its service queue is created. Real RabbitMQ tests publish both old and new commands after a
broker restart and receive them from the same service queue, and prove the rejected owner leaves no
queue behind.

This slice does not add provider registration, sending, receiving, events or requests. The Release
solution build succeeds with no warnings or errors and all 151 tests pass.

Approved and committed as `e4e713f`.

### RabbitMQ provider registration — implemented, verified and approved

Applications can select the provider with `bus.UseRabbitMq(connectionString)`. The extension only
registers the DI-owned RabbitMqTransport; the common TinyBusRuntime remains the single hosted service
and awaits provider initialization before host startup completes.

A real-host RabbitMQ test proves that the service queue already exists when `host.StartAsync()`
returns. Provider-specific workers, sending, receiving and additional RabbitMQ configuration remain
outside this slice. The Release solution build succeeds with no warnings or errors and all 152 tests
pass.

Approved and committed as `9dcab2b`.

### PostgreSQL persistence foundation — implemented, verified and approved

TinyBus.PostgreSql depends directly on Npgsql and owns a small internal migration mechanism derived
from TinyEvents. Migration machinery lives in `Migrations`, while versioned schema changes live in
its `Migrations` subfolder, matching the TinyEvents provider layout. A session advisory lock
serializes concurrent migrators. Applied versions, names and
SHA-256 checksums are stored in `tinybus.schema_migrations`; changed history and provider/database
version drift fail instead of silently changing an applied migration. Each migration and its history
entry commit in one transaction.

The first migration creates `tinybus.command_owners`, keyed by contract name and version, with
database constraints for valid contract, version and service values. Real PostgreSQL 17 tests prove
fresh and repeated migration, concurrent initialization, checksum drift rejection, and transactional
rollback of a failing later migration without losing prior committed history. Automatic provider
startup migration and command ownership reconciliation remain later slices. The Release solution
build succeeds with no warnings or errors and all 40 PostgreSQL provider tests pass.

Approved and committed as `fa56b80`.

### PostgreSQL command ownership reconciliation — implemented, verified and approved

The internal PostgreSqlTopologyReconciler persists command ownership directly through Npgsql. One
transaction adds or confirms every command in a ServiceTopology. `ON CONFLICT DO NOTHING` arbitrates
concurrent claims through the table's primary key; an existing row from the same service is
idempotent, while a different owner rejects and rolls back the complete contribution. Reconciliation
never deletes declarations absent from the current manifest.

Real PostgreSQL 17 tests prove same-service rolling replicas preserve newer commands, ownership
survives reconstruction of the reconciler, a conflict retains its original owner, a later conflict
rolls back commands inserted earlier in the same contribution, and concurrent different-service
claims produce exactly one owner and one rejection. Route loading, provider initialization,
registration and automatic migration remain later slices. All 44 PostgreSQL provider tests pass.

Approved and committed as `fa56b80`.

### PostgreSQL command route loading — implemented, verified and approved

The internal PostgreSqlCommandRouteSource reads only requested command owners and returns passive
CommandRoute facts. One parameterized PostgreSQL query joins the ownership table with parallel
contract-name and version arrays; duplicate requirements are collapsed before I/O and results are
ordered deterministically. Empty requirements return without opening a database connection.

The source does not decide whether a missing route is an error. Provider initialization will compare
the returned facts with its required contracts before publishing a CommandRouteCache snapshot. Real
PostgreSQL 17 tests cover filtered loading, distinct contract versions, duplicate requirements,
missing routes, empty requirements and cancellation before I/O. All 48 PostgreSQL provider tests pass.

Approved and committed as `22a39c4`.

### PostgreSQL transport initialization — implemented, verified and approved

The internal PostgreSqlTransport now owns the provider readiness sequence. Initialization migrates
the provider schema, reconciles the service topology, loads required command routes, validates that
every requirement has an owner and only then publishes the immutable CommandRouteCache snapshot.
No cache becomes visible after a reconciliation conflict or missing route, while successfully
reconciled additive facts remain durable for a later retry.

Outbound usage discovery remains deferred, so production registration will initially supply an
empty required-contract set rather than introduce a manually maintained list. Direct transport tests
against PostgreSQL 17 prove automatic migration plus successful route publication, missing-route
failure after durable reconciliation, and ownership-conflict failure before cache publication. DI
registration remains the next slice. All 51 PostgreSQL provider tests pass.

Approved by the user's request to commit and continue.

### Later: outbound requirements from IBus usage — deferred

Analyze semantic IBus invocations in the generator. Command route requirements come from concrete
SendAsync message types, using the same contract identity rules as inbound messages. Requirements
must compose across referenced assemblies so callers in libraries contribute to the root host.

Agree portable outbound metadata and the handling of generic helpers where the concrete message
type is unavailable before implementation. Do not infer requirements by method spelling, reuse
inbound handler descriptors for outbound usage, or silently omit unresolved requirements. Event
publication and request/reply have different routing semantics and need to retain that distinction.

The user explicitly deferred outbound discovery and rejected an outbound-command list in application
registration. Do not add RequireCommand as its fallback. If unresolved usages eventually require an
explicit declaration, design it near the usage or helper that owns the dependency. That shape is
not yet agreed. Registration remains focused on configuring the service.
