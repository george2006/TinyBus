namespace TinyBus.SourceGen.Model;

internal sealed class SourceLocation
{
    public SourceLocation(int treeIndex, int start, int length)
    {
        TreeIndex = treeIndex;
        Start = start;
        Length = length;
    }

    public int TreeIndex { get; }

    public int Start { get; }

    public int Length { get; }

    public override bool Equals(object? obj)
    {
        return obj is SourceLocation other
            && TreeIndex == other.TreeIndex
            && Start == other.Start
            && Length == other.Length;
    }

    public override int GetHashCode()
    {
        return (TreeIndex, Start, Length).GetHashCode();
    }
}
