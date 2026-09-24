using System;

namespace TinyBus.SourceGen.Model;

internal sealed class MessageTypeAnalysis : IEquatable<MessageTypeAnalysis>
{
    public MessageTypeAnalysis(string displayName, string typeName, string identity)
    {
        DisplayName = displayName;
        TypeName = typeName;
        Identity = identity;
    }

    public string DisplayName { get; }

    public string TypeName { get; }

    public string Identity { get; }

    public bool Equals(MessageTypeAnalysis? other)
    {
        return other is not null
            && DisplayName == other.DisplayName
            && TypeName == other.TypeName
            && Identity == other.Identity;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as MessageTypeAnalysis);
    }

    public override int GetHashCode()
    {
        return (DisplayName, TypeName, Identity).GetHashCode();
    }
}
