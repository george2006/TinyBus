using System;

namespace TinyBus;

internal readonly record struct EventHandlerResult(
    Type HandlerType,
    bool Succeeded);
