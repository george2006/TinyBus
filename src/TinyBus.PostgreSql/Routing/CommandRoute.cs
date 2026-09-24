using TinyBus;

namespace TinyBus.PostgreSql;

internal readonly record struct CommandRoute(
    ContractIdentity Contract,
    ServiceIdentity Service);
