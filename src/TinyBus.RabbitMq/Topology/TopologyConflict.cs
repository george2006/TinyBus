using TinyBus;

namespace TinyBus.RabbitMq;

internal sealed record TopologyConflict(
    ContractIdentity Contract,
    ServiceIdentity ExistingOwner,
    ServiceIdentity CandidateOwner);
