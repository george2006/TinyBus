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
        return Run(compilation, assertCompilationSucceeds);
    }

    public static GeneratorDriverRunResult Run(
        CSharpCompilation compilation,
        bool assertCompilationSucceeds = true)
    {
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
        return Execute<T>(compilation);
    }

    public static GeneratedSourceResult GetGeneratedSource(
        GeneratorDriverRunResult run,
        string hintName)
    {
        var result = Assert.Single(run.Results);
        var source = Assert.Single(
            result.GeneratedSources,
            generated => generated.HintName == hintName);

        return source;
    }

    public static T Execute<T>(CSharpCompilation compilation, params byte[][] referencedAssemblies)
    {
        using var stream = new MemoryStream(CompileImage(compilation));
        var loadContext = new AssemblyLoadContext("TinyBusConsumer", isCollectible: true);
        try
        {
            loadContext.LoadFromAssemblyPath(typeof(IBusManifest).Assembly.Location);
            foreach (var referencedAssembly in referencedAssemblies)
            {
                using var referenceStream = new MemoryStream(referencedAssembly);
                loadContext.LoadFromStream(referenceStream);
            }

            var assembly = loadContext.LoadFromStream(stream);
            var run = assembly.GetType("Scenario")!.GetMethod("Run")!;
            return Assert.IsType<T>(run.Invoke(null, null));
        }
        finally
        {
            loadContext.Unload();
        }
    }

    public static MetadataReference CompileReference(
        string assemblyName,
        params string[] sources)
    {
        var compilation = CreateCompilation(assemblyName, sources);
        return MetadataReference.CreateFromImage(CompileImage(compilation));
    }

    public static byte[] CompileImage(CSharpCompilation compilation)
    {
        var driver = CSharpGeneratorDriver.Create(new TinyBusSourceGenerator().AsSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        AssertCompiles(output);

        using var stream = new MemoryStream();
        var emitted = output.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));

        return stream.ToArray();
    }

    public static CSharpCompilation CreateCompilationWithReferences(
        string assemblyName,
        IEnumerable<MetadataReference> references,
        params string[] sources)
    {
        return CreateCompilation(
            assemblyName,
            References.AddRange(references),
            sources);
    }

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        params string[] sources)
    {
        return CreateCompilation(assemblyName, References, sources);
    }

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        IEnumerable<MetadataReference> references,
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
            references,
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
