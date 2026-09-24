namespace TinyBus;

internal readonly record struct CommandRoute(
    ContractIdentity Contract,
    ServiceIdentity Service);
