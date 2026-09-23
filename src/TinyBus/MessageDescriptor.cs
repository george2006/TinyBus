using System;

namespace TinyBus;

public sealed record MessageDescriptor(
    ContractIdentity Contract,
    Type MessageType,
    Type HandlerType,
    MessageKind Kind,
    Type? ResponseType = null);
