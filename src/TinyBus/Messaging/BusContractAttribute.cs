using System;

namespace TinyBus;

/// <summary>
/// Gives a message a stable wire identity that does not change when its CLR namespace or type name
/// is refactored. Without this attribute TinyBus uses the fully-qualified CLR type name.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct,
    AllowMultiple = false,
    Inherited = false)]
public sealed class BusContractAttribute : Attribute
{
    public BusContractAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
    }

    public string Name { get; }

    public int Version { get; init; } = 1;
}
