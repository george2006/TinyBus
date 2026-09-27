using System;

namespace TinyBus;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class IncomingMiddlewareAttribute : Attribute
{
    public IncomingMiddlewareAttribute(int order)
    {
        Order = order;
    }

    public int Order { get; }
}
