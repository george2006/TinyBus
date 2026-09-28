# Retries and Dead Letters

TinyBus retries a command when its incoming middleware or handler throws. Core decides whether another
attempt is allowed; the selected transport performs the durable settlement.

## Configuration

Configure the policy with the bus:

```csharp
services.AddTinyBus(bus =>
{
    bus.Service("payments");

    bus.RetryOptions.MaximumAttempts = 5;
    bus.RetryOptions.MinimumDelay = TimeSpan.FromSeconds(1);
    bus.RetryOptions.MaximumDelay = TimeSpan.FromSeconds(30);

    bus.UsePostgreSql(connectionString);
});
```

Defaults:

| Option | Default | Meaning |
| --- | --- | --- |
| `MaximumAttempts` | `5` | Total executions, including the first attempt |
| `MinimumDelay` | `1 second` | Delay multiplier for the first failure |
| `MaximumDelay` | `30 seconds` | Upper bound for a calculated delay |

`MaximumAttempts` must be greater than zero. Delays cannot be negative, and the maximum must be at
least the minimum.

## Delay calculation

The current policy uses bounded linear backoff:

```text
delay = min(minimumDelay * failedAttempt, maximumDelay)
```

With a one-second minimum and five-second maximum, failed attempts wait for 1, 2, 3, 4, and then at
most 5 seconds.

When the current attempt equals `MaximumAttempts`, TinyBus dead-letters the delivery instead of
scheduling another attempt.

## Processing flow

For each delivery, the runtime:

1. executes the generated incoming pipeline once;
2. completes the delivery when execution succeeds;
3. calculates the next delay when execution throws and attempts remain;
4. asks the transport to schedule the retry;
5. asks the transport to dead-letter the final failed attempt.

Each attempt gets a new DI scope. TinyBus does not loop around a handler inside one receive call.

## PostgreSQL behavior

A retry atomically increments `failed_attempts`, clears the current claim, and sets
`available_at_utc`. Other workers cannot claim the row before that timestamp.

On exhaustion, one statement removes the owned row from `tinybus.command_messages` and inserts it into
`tinybus.dead_lettered_command_messages`. The stored failure contains:

- the original message ID and sequence;
- destination service;
- contract name and version;
- payload, correlation, causation, and application headers;
- original enqueue time and final dead-letter time;
- total failed attempts;
- exception type, message, and details.

The move is transactional: the command cannot disappear between the active and dead-letter tables.

## RabbitMQ behavior

RabbitMQ uses quorum queue delivery limits and delayed retry arguments. Rejecting a failed delivery
with requeue enabled lets the broker schedule its next attempt and maintain the delivery count.

On exhaustion, TinyBus publishes a copy to the service dead-letter queue with publisher confirms,
then acknowledges the original delivery. The copy preserves the message properties and adds headers
for:

- failed queue;
- failed attempt;
- exception type;
- exception message;
- exception details.

The original is acknowledged only after the enriched dead-letter publish succeeds.

RabbitMQ queue arguments are immutable declaration state. Every replica of one service must use the
same retry settings for its shared queue.

## Shutdown

Host shutdown cancellation is not a failed attempt. TinyBus abandons the delivery so the transport
can make it available again:

- PostgreSQL clears the active claim;
- RabbitMQ negatively acknowledges and requeues the delivery.

## Settlement failures

If completion, retry scheduling, or dead-letter settlement fails, TinyBus logs the settlement error.
The durable provider state remains authoritative: a PostgreSQL claim eventually expires, while an
unacknowledged RabbitMQ delivery can be redelivered.

## Inspection and recovery

Dead-letter storage is durable and provider-owned. A public inspection, repair, and replay API is not
implemented yet. That capability will be designed separately so recovery does not leak provider
details into the normal message pipeline.
