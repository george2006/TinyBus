using System;

namespace TinyBus.SourceGen.Model;

internal sealed class ContractAnalysis : IEquatable<ContractAnalysis>
{
    public ContractAnalysis(
        string? name,
        int version,
        SourceLocation nameLocation,
        SourceLocation versionLocation)
    {
        Name = name;
        Version = version;
        NameLocation = nameLocation;
        VersionLocation = versionLocation;
    }

    public string? Name { get; }

    public int Version { get; }

    public SourceLocation NameLocation { get; }

    public SourceLocation VersionLocation { get; }

    public bool Equals(ContractAnalysis? other)
    {
        return other is not null
            && Name == other.Name
            && Version == other.Version
            && NameLocation.Equals(other.NameLocation)
            && VersionLocation.Equals(other.VersionLocation);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as ContractAnalysis);
    }

    public override int GetHashCode()
    {
        return (Name, Version, NameLocation, VersionLocation).GetHashCode();
    }
}
