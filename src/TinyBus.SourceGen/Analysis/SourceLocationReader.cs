using System.Linq;
using Microsoft.CodeAnalysis;
using TinyBus.SourceGen.Model;

namespace TinyBus.SourceGen.Analysis;

internal static class SourceLocationReader
{
    public static SourceLocation Read(Compilation compilation, Location location)
    {
        var treeIndex = compilation.SyntaxTrees.TakeWhile(tree => tree != location.SourceTree).Count();
        return new SourceLocation(treeIndex, location.SourceSpan.Start, location.SourceSpan.Length);
    }
}
