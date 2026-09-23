using System;

namespace TinyBus;

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
