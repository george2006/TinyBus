using System;

namespace TinyBus.SourceGen.Model;

internal sealed class MiddlewareAnalysis : IEquatable<MiddlewareAnalysis>
{
    public MiddlewareAnalysis(
        string typeName,
        int order,
        bool isConcrete,
        bool implementsMiddleware,
        SourceLocation location)
    {
        TypeName = typeName;
        Order = order;
        IsConcrete = isConcrete;
        ImplementsMiddleware = implementsMiddleware;
        Location = location;
    }

    public string TypeName { get; }

    public int Order { get; }

    public bool IsConcrete { get; }

    public bool ImplementsMiddleware { get; }

    public SourceLocation Location { get; }

    public bool Equals(MiddlewareAnalysis? other)
    {
        return other is not null
            && TypeName == other.TypeName
            && Order == other.Order
            && IsConcrete == other.IsConcrete
            && ImplementsMiddleware == other.ImplementsMiddleware
            && Location.Equals(other.Location);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as MiddlewareAnalysis);
    }

    public override int GetHashCode()
    {
        return (TypeName, Order, IsConcrete, ImplementsMiddleware, Location).GetHashCode();
    }
}
