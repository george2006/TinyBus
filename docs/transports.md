# Transports

TinyBus runs with exactly one transport provider per host. The provider owns physical topology,
routing, persistence, receiving, retry scheduling, and dead-letter storage.

Core owns the common receive loop and generated message pipeline.

## Choosing a provider

Use PostgreSQL when the database is the desired durable queue and operational store:

```csharp
using TinyBus.PostgreSql;

services.AddTinyBus(bus =>
{
    bus.Service("payments");
    bus.UsePostgreSql(postgreSqlConnectionString);
});
```

Use RabbitMQ when broker-native routing and delivery are a better fit:

```csharp
using TinyBus.RabbitMq;

services.AddTinyBus(bus =>
{
    bus.Service("payments");
    bus.UseRabbitMq("amqp://guest:guest@localhost:5672");
});
```

A missing or duplicate provider fails service resolution. Both providers reject messaging before
successful initialization.

## Startup readiness

TinyBus awaits provider initialization before the hosted receive loop starts. Each provider must:

1. reconcile the service's additive topology;
2. validate command ownership;
3. prepare its physical messaging resources;
4. return only when sending and receiving are safe.

If any step fails, host startup fails. The application never becomes ready with an empty or partially
initialized routing state.

## PostgreSQL

The PostgreSQL provider applies its own versioned migrations during initialization. An advisory lock
serializes migrations across replicas, and migration names and checksums protect the history from
silent drift.

The provider creates the `tinybus` schema and these operational tables:

| Table | Responsibility |
| --- | --- |
| `schema_migrations` | Applied provider schema versions |
| `command_owners` | One service owner per command contract and version |
| `command_messages` | Active command deliveries, claims, and retry availability |
| `dead_lettered_command_messages` | Exhausted messages and their final failure |

Sending resolves command ownership inside the insert statement. A command without a registered owner
is rejected.

Receivers claim ordered batches with `FOR UPDATE SKIP LOCKED`. Each claim has a lease, so multiple
replicas and runtime workers act as competing consumers without processing the same available row.
The runtime's available capacity controls the maximum claim batch size.

The command lease lasts five minutes by default. Configure it for the longest expected handler
execution plus settlement time:

```csharp
bus.UsePostgreSql(postgreSqlConnectionString, postgreSql =>
{
    postgreSql.CommandLeaseDuration = TimeSpan.FromMinutes(10);
});
```

The provider does not renew active leases. When a lease expires, another receiver can reclaim the
command even if its previous handler is still running. This is part of the at-least-once contract;
handlers must tolerate repeated execution, and the lease should be sized for the workload.

Completion deletes the claimed row. A retry updates its failed-attempt count and next availability.
Dead-lettering inserts the complete failure record and removes the active row in one database
transaction.

The database user needs permission to create and modify the `tinybus` schema and to read and write its
tables.

## RabbitMQ

The RabbitMQ provider uses deterministic physical names:

| Resource | Name |
| --- | --- |
| Command exchange | `tinybus.commands` |
| Command routing key | `{contract-name}.v{version}` |
| Service queue | `tinybus.{service-name}` |
| Dead-letter exchange | `tinybus.dead-letters` |
| Service dead-letter queue | `tinybus.{service-name}.dead-letter` |
| Topology journal | `tinybus.topology` |

Service and dead-letter queues are durable quorum queues. The provider publishes persistent command
messages with publisher confirmations enabled.

During initialization, the provider replays an additive RabbitMQ stream that records service
declarations. This validates that two services have not claimed the same command. It then declares the
exchange, queues, and command bindings required by the current service.

Routing and ownership validation remain separate concerns. A deterministic routing key chooses a
physical route; the topology journal proves that only one logical service owns the command.

RabbitMQ delayed retries and delivery limits are queue declaration arguments. All replicas using the
same service queue must use the same retry configuration. Changing those immutable arguments requires
an explicit queue migration strategy.

The current RabbitMQ implementation targets RabbitMQ 4.3 capabilities used by delayed delivery and
at-least-once dead-lettering.

## Additive topology

Both providers treat a service declaration as additive. Reconciliation can insert or confirm a fact;
absence from the current manifest never removes an existing fact.

For example, if payments v2 adds `RefundPayment`, a still-running payments v1 replica can confirm its
older declarations without deleting the new command. Retirement is deliberately deferred until
TinyBus has an explicit rolling-deployment policy.

## Common delivery contract

Providers return deliveries in batches constrained by `ReceiveCapacity`. Every delivery exposes its
envelope and one-based attempt number, plus four settlement operations:

- complete after successful pipeline execution;
- schedule a delayed retry after a processing failure;
- dead-letter after retry exhaustion;
- abandon when processing is interrupted by shutdown.

This contract keeps the runtime common without pretending that a database row and a broker delivery
have the same internal mechanics.
