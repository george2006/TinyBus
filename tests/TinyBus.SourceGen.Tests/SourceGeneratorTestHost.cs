using System.Collections.Immutable;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TinyBus.SourceGen.Tests;

internal static class SourceGeneratorTestHost
{
    private static readonly ImmutableArray<MetadataReference> References = CreateReferences();

    public static GeneratorDriverRunResult Run(params string[] sources)
    {
        var compilation = CreateCompilation(sources);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new TinyBusSourceGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        var result = Assert.Single(driver.GetRunResult().Results);
        Assert.Null(result.Exception);
        AssertCompiles(output);

        return driver.GetRunResult();
    }

    public static T Execute<T>(params string[] sources)
    {
        var compilation = CreateCompilation(sources);
        var driver = CSharpGeneratorDriver.Create(new TinyBusSourceGenerator().AsSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        AssertCompiles(output);

        using var stream = new MemoryStream();
        var emitted = output.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));

        var loadContext = new AssemblyLoadContext("TinyBusConsumer", isCollectible: true);
        try
        {
            loadContext.LoadFromAssemblyPath(typeof(IBusManifest).Assembly.Location);
            stream.Position = 0;
            var assembly = loadContext.LoadFromStream(stream);
            var run = assembly.GetType("Scenario")!.GetMethod("Run")!;
            return Assert.IsType<T>(run.Invoke(null, null));
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var trees = sources.Select((source, index) => CSharpSyntaxTree.ParseText(
            source,
            path: $"Source{index}.cs"));
        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable);

        return CSharpCompilation.Create(
            $"TinyBusConsumer_{Guid.NewGuid():N}",
            trees,
            References,
            options);
    }

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        var platformAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator);

        return platformAssemblies
            .Append(typeof(IBusManifest).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray<MetadataReference>();
    }

    private static void AssertCompiles(Compilation compilation)
    {
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        Assert.True(!errors.Any(), string.Join(Environment.NewLine, errors));
    }
}
