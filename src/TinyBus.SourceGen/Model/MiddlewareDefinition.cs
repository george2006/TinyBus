using System;

namespace TinyBus.SourceGen.Model;

internal sealed class MiddlewareDefinition : IEquatable<MiddlewareDefinition>
{
    public MiddlewareDefinition(string typeName, int order)
    {
        TypeName = typeName;
        Order = order;
    }

    public string TypeName { get; }

    public int Order { get; }

    public bool Equals(MiddlewareDefinition? other)
    {
        return other is not null
            && TypeName == other.TypeName
            && Order == other.Order;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as MiddlewareDefinition);
    }

    public override int GetHashCode()
    {
        return (TypeName, Order).GetHashCode();
    }
}
