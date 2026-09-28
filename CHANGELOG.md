# Changelog

## 0.1.0-alpha.1 - 2026-09-28

### Added

- Reliable fire-and-forget commands through PostgreSQL and RabbitMQ.
- Compile-time handler discovery, validation, registration, and multi-assembly manifest composition.
- Generated incoming middleware and command dispatch.
- Additive service topology with unique command ownership validation.
- Startup readiness gated by provider initialization.
- Configurable competing-consumer capacity, retries, and dead-letter handling.
- PostgreSQL migrations, durable command storage, leased claims, and transactional dead-letter moves.
- RabbitMQ deterministic routing, topology journal, quorum queues, publisher confirms, delayed retries,
  and durable dead-letter queues.

### Limitations

- Event publishing and request/reply are not implemented.
- Sagas, scheduled messages, outbox integration, dead-letter recovery tooling, and production support
  guarantees are not included.
- APIs may change before a stable release.

### Compatibility

- Every TinyBus package belongs to one release train and must be upgraded together.
- RabbitMQ requires the 4.3 capabilities used for delayed retry and at-least-once dead-lettering.
