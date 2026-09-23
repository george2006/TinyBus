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
the multi-assembly composition design, native TinyBus handler execution, the PostgreSQL runtime and
the proposed `TinyBus.TinyEvents` adapter.

Architectural decision: TinyDispatcher will not execute TinyBus handlers. Its in-process dispatch
semantics do not represent distributed command and event delivery. TinyBus will own its command,
event and request execution semantics and generate the required invocation plumbing when that
runtime slice begins.

`TinyBus.TinyEvents` remains an optional integration package to evaluate and design before coding.
Its purpose is to reuse the existing TinyEvents outbox implementation; TinyBus core will not depend
on TinyEvents.

Approved decision: multi-assembly composition will use generated compile-time contribution metadata
through `BusMessageContributionAttribute`. TinyBus will not use the TinyFlags-style mutable runtime
registry because topology completeness and cross-assembly conflicts must be known at compilation.

Transport boundary decision: PostgreSQL is the first implementation, not the core abstraction.
PostgreSQL schema, locks, leases, polling and any optional `LISTEN/NOTIFY` wake-up remain private to
`TinyBus.PostgreSql`. Correctness cannot depend on notifications, and the first version may omit
them until measurements justify the optimization. The common seam must also fit a broker-based
transport before it is stabilized.

Mandatory checkpoint: after drafting the common transport interfaces and before starting the first
PostgreSQL slice, compare those interfaces against PostgreSQL and at least one broker transport.
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

### Next session

Start with multi-assembly topology slice 3: generate one deterministic composed manifest in the
root assembly. Compose the local manifest with the distinct public manifests identified by the
referenced contributions. Use contribution metadata for compile-time reasoning while avoiding
direct generated references to potentially internal handler types. Keep cross-assembly conflict
diagnostics in slice 4.
