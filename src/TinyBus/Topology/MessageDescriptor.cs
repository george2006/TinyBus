using System;

namespace TinyBus;

/// <summary>
/// Describes one generated message-handler contribution in a service topology.
/// </summary>
public sealed record MessageDescriptor(
    ContractIdentity Contract,
    Type MessageType,
    Type HandlerType,
    MessageKind Kind,
    Type? ResponseType = null);
