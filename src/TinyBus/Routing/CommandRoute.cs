namespace TinyBus;

public readonly record struct CommandRoute(
    ContractIdentity Contract,
    ServiceIdentity Service);
