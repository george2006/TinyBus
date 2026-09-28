# Documentation

Start with **Getting Started** to send and consume a command. Read **Architecture** next to see where
Core ends and each transport begins.

## Core documentation

- [Getting Started](getting-started.md)
- [Architecture](architecture.md)
- [Message Contracts](message-contracts.md)
- [Incoming Pipeline](incoming-pipeline.md)
- [Transports](transports.md)
- [Retries and Dead Letters](retries-and-dead-letters.md)
- [Source Generator](source-generator.md)
- [Design Decisions](design-decisions.md)

## Current scope

TinyBus is an alpha. Distributed commands are implemented end to end with PostgreSQL and RabbitMQ.
The public event and request/reply contracts are present, and their distributed runtime paths remain
under development.

The documentation describes implemented behavior unless a section explicitly marks something as
planned.
