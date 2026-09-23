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

### Slice 5: generated manifest guarantees — implemented, awaiting review

Lock down the generated manifest behavior before diagnostics: output remains identical when source
file order changes, multiple local handlers for one event are retained, and an assembly with no
handlers receives an empty manifest without runtime registration or assembly scanning. No public
API or generator architecture changes are expected.

All three guarantees passed without production changes, confirming that planning already owns the
ordering and generation needs no runtime discovery path. The full solution passes eighteen tests
with no warnings.

Approved and committed as `5ceaf16`.

### Slice 6: contract identity diagnostics — implemented, awaiting review

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

### Slice 7: handler topology diagnostics — implemented, awaiting review

Validate the complete set of locally valid handlers after individual contract validation. Report
duplicate command handlers, duplicate request handlers and one CLR message type used with
conflicting semantics. Multiple handlers for an event remain valid. Generation keeps all valid
descriptors; a topology error fails compilation rather than selecting a handler.

Three focused tests cover duplicate command handlers, duplicate request handlers and conflicting
semantics within one handler class. Diagnostics point at every distinct handler involved and are
ordered deterministically. The existing executable multiple-event-handler test remains green. The
complete solution passes twenty-six tests with no warnings.

Approved and committed as `fa24921`.

### Slice 8: compile-time developer experience samples — implemented, awaiting review

Add the three planned sample assemblies. Contracts contains ordinary records and one stable
`[BusContract]` identity. Payments consumes command, event and request messages; Orders independently
consumes the same event. The samples only prove declaration and generation ergonomics—there is no
runtime transport or simulated communication.

The complete solution builds all three samples with no warnings. Inspected generated sources show
one event descriptor in Orders and command, request-with-response and event descriptors in Payments;
both independently consume the stable `orders.order-placed` contract. No generated topology leaks
between assemblies.

### Later slices — intent only

Events and requests, explicit contract naming, diagnostics and samples will each be sliced before
implementation. PostgreSQL and all distributed runtime behavior remain outside this bootstrap
feature.
