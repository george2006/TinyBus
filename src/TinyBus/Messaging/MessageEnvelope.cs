using System;
using System.Collections.Generic;

namespace TinyBus;

public sealed record MessageEnvelope(
    Guid MessageId,
    ContractIdentity Contract,
    string Payload,
    string? CorrelationId = null,
    string? CausationId = null,
    IReadOnlyDictionary<string, string>? Headers = null);
