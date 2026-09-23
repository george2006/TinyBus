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

### Slice 2: first source-generated command manifest — implemented, awaiting review

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

### Later slices — intent only

Events and requests, explicit contract naming, diagnostics and samples will each be sliced before
implementation. PostgreSQL and all distributed runtime behavior remain outside this bootstrap
feature.
