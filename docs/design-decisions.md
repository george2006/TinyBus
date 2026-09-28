# Design Decisions

This document records the constraints that shape TinyBus and the reason each one exists.

## Compile-time composition

Handlers, contracts, middleware order, registrations, and incoming dispatch are known at build time.
TinyBus generates that structure instead of reconstructing it through runtime assembly scanning.

This makes invalid topology a compiler problem and leaves a small, explicit runtime path.

## Commands, events, and requests have distinct contracts

TinyBus uses three handler interfaces because the message kinds have different semantics:

- a command has one owner and one handler;
- an event has independent subscribers;
- a request has one handler and one typed response.

A single generic consumer interface would hide those rules and move their validation into runtime
configuration.

## Messages do not require marker interfaces

Handler declarations identify message types. Application contracts remain ordinary records or
classes, and shared contract assemblies do not need framework-specific inheritance.

`BusContractAttribute` exists only when a stable wire identity must be explicit.

## The host owns final composition

Libraries publish their local message facts. The root application composes the service manifest and
incoming pipeline from local and referenced contributions.

This preserves module encapsulation while keeping one final topology for the process.

## One transport per host

A TinyBus host represents one logical service using one delivery model. Allowing several active
transports would make initialization, routing, settlement, and readiness ambiguous.

Applications that bridge transports should model the bridge explicitly as application behavior.

## One common runtime

TinyBus owns the hosted receive loop, available capacity, pipeline execution, retry decision, and
shutdown behavior. Providers implement the physical transport operations behind `ITransport` and
`ITransportDelivery`.

This keeps application execution consistent without requiring providers to run unrelated hosted
services.

## Provider-native mechanics

PostgreSQL and RabbitMQ solve routing and durability differently. TinyBus shares the semantic boundary
and lets each provider use its native strengths.

Core therefore has no mandatory route cache, management API, SQL storage model, or broker topology
model.

## Routing and ownership are separate

A deterministic physical address can route a command while several services still claim it. Every
provider validates the one-owner command invariant during initialization, independently from its send
route.

## Startup is gated by readiness

The host is not ready until the provider has reconciled topology, validated ownership, and prepared
safe messaging resources.

TinyBus awaits initialization during hosted-service startup. It does not expose an empty routing state
that races the first send.

## Topology reconciliation is additive

Absence from a replica's manifest is not deletion intent. This prevents an older replica in a rolling
deployment from erasing declarations introduced by a newer replica.

Removal and retirement need a future explicit revision policy.

## The incoming boundary is an envelope

Transport reception produces a `MessageEnvelope`, before the runtime knows which CLR command it
contains. Middleware therefore works with the envelope. Generated dispatch deserializes the typed
command at the handler boundary.

This keeps transport concerns out of handlers and typed application concerns out of the receive loop.

## The pipeline is generated

Middleware ordering and the final command target are compile-time facts. TinyBus emits direct calls
instead of building a delegate chain at runtime.

The generated path is deterministic, allocation-conscious, and readable in a debugger.

## One scope per delivery attempt

Middleware and the handler share one DI scope for an attempt. A retry creates a new attempt and a new
scope.

This aligns dependency lifetime with one unit of message processing.

## Retry policy and settlement are separate

Core decides whether a failed attempt retries or exhausts the policy. The provider schedules or
dead-letters the durable delivery.

The decision remains consistent across transports while each provider preserves its transactional and
operational guarantees.

## Shutdown does not consume an attempt

Cancellation caused by host shutdown abandons the delivery. It is not treated as a handler failure and
does not advance the retry policy.

## Recovery tooling is a separate capability

Dead-letter inspection and replay will need provider-aware operations and explicit safety rules. Those
APIs are deferred rather than mixed into normal sending and receiving before their semantics are
clear.
