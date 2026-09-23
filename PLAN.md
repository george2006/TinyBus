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

### Slice 4: contract identity generation — implemented, awaiting review

Read valid `[BusContract]` declarations during analysis and carry their name and version as plain
model values into generation. Messages without the attribute keep the readable fully-qualified CLR
type name and version one. Manifest ordering includes contract version. Invalid explicit identities
remain the following diagnostics slice.

Four executable generator tests cover the default identity, explicit name, explicit version and
safe C# emission of quoted/control characters in a valid name. The complete solution passes fifteen
tests with no warnings.

### Later slices — intent only

Events and requests, explicit contract naming, diagnostics and samples will each be sliced before
implementation. PostgreSQL and all distributed runtime behavior remain outside this bootstrap
feature.
