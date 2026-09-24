using System;

namespace TinyBus;

public readonly record struct EventHandlerResult(
    Type HandlerType,
    bool Succeeded);
