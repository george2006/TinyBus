using System.Collections.Immutable;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TinyBus.SourceGen.Tests;

internal static class SourceGeneratorTestHost
{
    private const string DefaultAssemblyName = "TinyBusConsumer";
    private static readonly ImmutableArray<MetadataReference> References = CreateReferences();

    public static GeneratorDriverRunResult Run(params string[] sources)
    {
        return Run(DefaultAssemblyName, assertCompilationSucceeds: true, sources);
    }

    public static GeneratorDriverRunResult RunForAssembly(
        string assemblyName,
        params string[] sources)
    {
        return Run(assemblyName, assertCompilationSucceeds: true, sources);
    }

    public static GeneratorDriverRunResult RunWithDiagnostics(params string[] sources)
    {
        return Run(DefaultAssemblyName, assertCompilationSucceeds: false, sources);
    }

    private static GeneratorDriverRunResult Run(
        string assemblyName,
        bool assertCompilationSucceeds,
        params string[] sources)
    {
        var compilation = CreateCompilation(assemblyName, sources);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new TinyBusSourceGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        var result = Assert.Single(driver.GetRunResult().Results);
        Assert.Null(result.Exception);
        if (assertCompilationSucceeds)
        {
            AssertCompiles(output);
        }

        return driver.GetRunResult();
    }

    public static T Execute<T>(params string[] sources)
    {
        var compilation = CreateCompilation(DefaultAssemblyName, sources);
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

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        params string[] sources)
    {
        var trees = sources.Select((source, index) => CSharpSyntaxTree.ParseText(
            source,
            path: $"Source{index}.cs"));
        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable);

        return CSharpCompilation.Create(
            assemblyName,
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
