using System;
using TinyBus;

namespace TinyBus.PostgreSql.Persistence;

internal sealed record ClaimedCommandMessage(
    long SequenceId,
    Guid ClaimId,
    int Attempt,
    MessageEnvelope Envelope);
