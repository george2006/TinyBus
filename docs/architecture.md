# Architecture

TinyBus has three parts:

- a small runtime core;
- a source generator that discovers and composes application code;
- transport providers that own physical messaging mechanics.

## Message flow

An outgoing command follows this path:

```text
application
  -> IBus.SendAsync
  -> MessageEnvelope
  -> selected transport
  -> durable provider storage
```

The owning service receives it through the common runtime:

```text
selected transport
  -> ITransportDelivery
  -> generated incoming middleware
  -> generated contract dispatch
  -> scoped command handler
  -> complete, retry, or dead letter
```

The runtime works with `MessageEnvelope`. Typed command deserialization happens only at the generated
handler boundary.

## Runtime core

Core owns the behavior shared by every provider:

- `IBus` as the application-facing messaging API;
- one hosted receive loop per service;
- concurrency capacity;
- incoming pipeline execution;
- retry policy decisions;
- delivery settlement coordination;
- host startup and shutdown behavior.

TinyBus accepts exactly one `ITransport` registration. The runtime calls
`ITransport.InitializeAsync` before starting its receive loop. The provider must complete every step
required for safe messaging before that call returns. A failed initialization fails host startup.

Each received delivery runs inside a new DI scope. Generated middleware and handlers are scoped to
that delivery.

## Source generator

The generator owns application structure that can be known at build time:

- handler and middleware discovery;
- handler shape and topology validation;
- scoped DI registrations;
- local assembly manifests;
- referenced assembly contributions;
- the host's composed service manifest;
- ordered incoming middleware;
- contract-to-handler command dispatch.

Referenced libraries publish structured manifest metadata. The root application reads those
contributions and emits the final manifest and pipeline. Runtime assembly scanning is unnecessary.

## Service topology

`ServiceTopology` describes the logical service and the messages it handles. A message descriptor
contains its stable contract identity, CLR message type, handler type, message kind, and optional
response type.

Topology reconciliation is additive. A running replica may add or confirm declarations, but an
absent declaration never means delete. This preserves a rolling-deployment invariant:

> An older replica cannot erase topology introduced by a newer replica.

Removal and retirement require an explicit future topology policy.

Commands have one owning service. Ownership validation is separate from routing: deterministic
physical routing does not by itself prevent two services from claiming the same command.

## Transport boundary

`ITransport` is the provider SPI. It exposes only the operations the common runtime needs:

- initialize the service topology;
- durably accept an outgoing command;
- receive a batch constrained by available runtime capacity.

`ITransportDelivery` represents one received attempt. It lets the common runtime complete, retry,
dead-letter, or abandon that attempt without knowing how the provider implements those operations.

The provider owns its connections, migrations, topology materialization, routing, delivery claims,
retry scheduling, and dead-letter storage.

## Why the providers differ

PostgreSQL stores command ownership and message rows in shared tables. Receivers claim available
rows in batches, and competing workers scale consumption for one logical service. Completion,
retry scheduling, and dead-lettering are database operations.

RabbitMQ derives deterministic command addresses and binds the owning service queue. It validates
ownership through an additive topology journal, then relies on native broker delivery. Retry and
dead-letter behavior use RabbitMQ queue capabilities.

The common SPI coordinates both without imposing a PostgreSQL route cache or a RabbitMQ management
API on Core.

## Failure boundary

A handler exception returns to the runtime. Core decides whether the attempt should retry or exhaust
the configured policy. The delivery then performs the provider-specific settlement.

Shutdown cancellation is different from processing failure. An interrupted delivery is abandoned so
the provider can make it available again. Settlement failures are logged because the provider still
owns the durable delivery state.
