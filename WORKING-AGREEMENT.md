# Working agreement

## Engineering standard

We are two principal engineers building TinyBus together. Prefer simple, robust, readable
object-oriented code. Good practices serve the product; they are not an exercise in purity.

- Objects represent concrete responsibilities and keep related behavior together.
- Read top down: entry point and main behavior first, implementation details below.
- Let orchestration code breathe. An entry point should read like an index of the behavior, with
  one clearly named step per line and whitespace between phases. Move pipeline mechanics and
  implementation detail into small methods below it so a reader can understand the complete flow
  before choosing which step to inspect.
- Keep methods small and focused on one responsibility, without fragmenting a readable flow.
- Use intention-revealing names, braces, early returns, and whitespace between logical steps.
- Give domain decisions explicit names. A reader should understand why a branch is taken without
  decoding several implementation checks joined by `&&` or `||`. Move those checks behind a
  well-named boolean or small predicate method so the main method reads top down. Keep a compound
  condition inline only when it is already obvious as one idea.
- Explain constraints and decisions in comments, rather than narrating obvious code.
- Accept small local duplication when sharing it would obscure the behavior.
- Make failure and cancellation behavior explicit. Do not claim guarantees we have not tested.

## Abstractions require discussion

Before creating a new abstraction, ask the user for permission and explain:

1. The current consumer and concrete problem.
2. The responsibility and proposed contract.
3. Why a concrete implementation or local code is insufficient.
4. The cost and simpler alternative.

Wait for approval before implementing that abstraction. Do not repeatedly ask for an already
approved contract unless its scope changes. Interfaces, base classes, wrappers, and extension
points must earn their place; do not create them for speculative reuse or mocking.

## Tests

- Test observable behavior, not private structure or sequences of mocked calls.
- Use real collaborators and real Roslyn compilations for generator tests.
- Check generated compilation, diagnostics, and executable behavior where relevant.
- Add focused regression tests for meaningful edge cases and discovered bugs.
- Do not add placeholder tests, tests of empty scaffolding, or assertions that merely mirror
  implementation details.
- Report exactly what ran, passed, failed, or remains unverified.

## Feature and slice workflow

Before implementing a feature, discuss its intended behavior and divide it into small, reviewable
slices with the user. Only the current feature gets an implementation breakdown; future features
stay as intent until we discuss them.

Before editing a slice, present its goal, existing code, proposed changes, behavioral checks, scope
boundaries, and decisions needed. An explicitly requested bootstrap can proceed within that scope
without asking for the same authorization again.

Then inspect the current files, implement the agreed slice, run appropriate checks, inspect the
resulting changes, and report the outcome. Stop for the user's review and approval before starting
another slice. Update `PLAN.md` to distinguish implementation from approval.

No commits, pushes, publishing, broad formatting, or unrelated cleanup unless requested.

## Generator structure

Use the Tiny Suite generators as references, not templates to copy wholesale.

- `Discovery` performs the initial syntax filtering.
- `Analysis` resolves Roslyn symbols and produces plain models.
- No Roslyn symbol may leave `Analysis`.
- `Generation` receives validated definitions and returns source text.
- Keep generation planning and source emission explicit and independent of Roslyn.
- Prefer incremental generation and deterministic output.

Introduce only the phases and types the agreed behavior needs. A phase does not need an interface
just because it has a name.
