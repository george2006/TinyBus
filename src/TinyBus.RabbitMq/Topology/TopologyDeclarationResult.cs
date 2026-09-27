namespace TinyBus.RabbitMq;

internal readonly record struct TopologyDeclarationResult(
    bool IsAccepted,
    TopologyConflict? Conflict);
