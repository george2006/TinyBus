using Microsoft.CodeAnalysis;

namespace TinyBus.SourceGen.Tests;

public sealed class ReferencedTopologyDiagnosticTests
{
    private const string Contracts = """
        namespace Contracts;
        public sealed record Message;
        public sealed record Envelope<T>;
        """;

    [Theory]
    [InlineData("Command", "Command", "TBUS003")]
    [InlineData("Request", "Request", "TBUS004")]
    [InlineData("Command", "Event", "TBUS005")]
    public void Rejects_conflicts_between_libraries_in_deterministic_order(
        string firstKind, string secondKind, string diagnosticId)
    {
        var contracts = CompileLibrary("Contracts", Contracts);
        var alpha = CompileLibrary("Alpha", Handler(firstKind), contracts);
        var beta = CompileLibrary("Beta", Handler(secondKind), contracts);

        var first = RunRoot(new[] { contracts, alpha, beta });
        var second = RunRoot(new[] { beta, alpha, contracts });

        var diagnostic = Assert.Single(first.Diagnostics);
        Assert.Equal(diagnosticId, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(Location.None, diagnostic.Location);
        Assert.Contains("Contracts.Message", diagnostic.GetMessage());
        Assert.Contains("Alpha::global::Handlers.Handler, Beta::global::Handlers.Handler", diagnostic.GetMessage());
        Assert.Equal(
            first.Diagnostics.Select(value => value.ToString()),
            second.Diagnostics.Select(value => value.ToString()));
    }

    [Theory]
    [InlineData("Command", "Command", "TBUS003")]
    [InlineData("Request", "Request", "TBUS004")]
    [InlineData("Command", "Event", "TBUS005")]
    public void Locates_local_handlers_and_identifies_referenced_conflicts(
        string localKind, string referencedKind, string diagnosticId)
    {
        var contracts = CompileLibrary("Contracts", Contracts);
        var library = CompileLibrary("Library", Handler(referencedKind), contracts);

        var run = RunRoot(new[] { contracts, library }, Handler(localKind, "LocalHandler"));

        Assert.Equal(2, run.Diagnostics.Length);
        Assert.All(run.Diagnostics, diagnostic =>
        {
            Assert.Equal(diagnosticId, diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        });
        var local = Assert.Single(run.Diagnostics.Where(diagnostic => diagnostic.Location.IsInSource));
        Assert.Equal("LocalHandler", local.Location.SourceTree!.GetText().ToString(local.Location.SourceSpan));
        var referenced = Assert.Single(run.Diagnostics.Where(diagnostic => !diagnostic.Location.IsInSource));
        Assert.Contains("Library::global::Handlers.Handler", referenced.GetMessage());
        Assert.Contains("Root::global::Handlers.LocalHandler", referenced.GetMessage());
    }

    [Fact]
    public void Orders_multiple_conflicts_independently_of_reference_and_source_order()
    {
        var contracts = CompileLibrary("Contracts", Contracts);
        var command = Handler("Command", "CommandHandler");
        var request = Handler("Request", "RequestHandler", "global::Contracts.Envelope<int>");
        var alpha = CompileLibrary("Alpha", command, contracts);
        var beta = CompileLibrary("Beta", request, contracts);

        var first = RunRoot(new[] { contracts, alpha, beta }, command, request);
        var second = RunRoot(new[] { beta, alpha, contracts }, request, command);

        Assert.Equal(4, first.Diagnostics.Length);
        Assert.Equal(2, first.Diagnostics.Count(diagnostic => diagnostic.Id == "TBUS003"));
        Assert.Equal(2, first.Diagnostics.Count(diagnostic => diagnostic.Id == "TBUS004"));
        Assert.Equal(
            first.Diagnostics.Select(DescribeDiagnostic),
            second.Diagnostics.Select(DescribeDiagnostic));
    }

    [Fact]
    public void Reports_each_local_handler_once_when_local_and_referenced_duplicates_overlap()
    {
        var contracts = CompileLibrary("Contracts", Contracts);
        var library = CompileLibrary("Library", Handler("Command"), contracts);

        var run = RunRoot(
            new[] { contracts, library },
            Handler("Command", "FirstHandler"),
            Handler("Command", "SecondHandler"));

        Assert.Equal(3, run.Diagnostics.Length);
        Assert.All(run.Diagnostics, diagnostic => Assert.Equal("TBUS003", diagnostic.Id));
        Assert.Equal(2, run.Diagnostics.Count(diagnostic => diagnostic.Location.IsInSource));
        Assert.Single(run.Diagnostics.Where(diagnostic => diagnostic.Location == Location.None));
    }

    [Theory]
    [InlineData("global::Messages.Payload")]
    [InlineData("global::Messages.Payload[]")]
    [InlineData("global::Contracts.Envelope<global::Messages.Payload>")]
    public void Does_not_confuse_message_types_with_the_same_name_from_different_assemblies(string messageType)
    {
        var contracts = CompileLibrary("Contracts", Contracts);
        var source = Handler("Command", messageType: messageType) + """

            namespace Messages { internal sealed record Payload; }
            """;
        var alpha = CompileLibrary("Alpha", source, contracts);
        var beta = CompileLibrary("Beta", source, contracts);
        var references = new[] { contracts, alpha, beta };
        var root = SourceGeneratorTestHost.CreateCompilationWithReferences(
            "Root", references.Select(image => MetadataReference.CreateFromImage(image)), """
            public static class Scenario
            {
                public static int Run() => new TinyBus.Generated.GeneratedTinyBusManifest().Messages.Count;
            }
            """);

        var run = SourceGeneratorTestHost.Run(root);

        Assert.Empty(run.Diagnostics);
        Assert.Equal(2, SourceGeneratorTestHost.Execute<int>(root, references));
    }

    [Fact]
    public void Retains_valid_shared_events_and_distinct_commands()
    {
        var contracts = CompileLibrary("Contracts", Contracts);
        var alpha = CompileLibrary("Alpha", Handler("Event"), contracts);
        var beta = CompileLibrary("Beta", Handler("Event"), contracts);

        var run = RunRoot(
            new[] { contracts, alpha, beta },
            Handler("Command", "CommandHandler", "global::Contracts.Envelope<int>"));

        Assert.Empty(run.Diagnostics);
    }

    private static GeneratorDriverRunResult RunRoot(byte[][] references, params string[] sources)
    {
        var compilation = SourceGeneratorTestHost.CreateCompilationWithReferences(
            "Root", references.Select(image => MetadataReference.CreateFromImage(image)), sources);

        // Topology diagnostics fail the build without removing descriptors or breaking generated C#.
        return SourceGeneratorTestHost.Run(compilation);
    }

    private static string DescribeDiagnostic(Diagnostic diagnostic)
    {
        var location = diagnostic.Location;
        var handler = location.SourceTree?.GetText().ToString(location.SourceSpan);
        return $"{diagnostic.Id}|{diagnostic.GetMessage()}|{handler}";
    }

    private static byte[] CompileLibrary(string assemblyName, string source, params byte[][] references)
    {
        var compilation = SourceGeneratorTestHost.CreateCompilationWithReferences(
            assemblyName, references.Select(image => MetadataReference.CreateFromImage(image)), source);

        Assert.Empty(SourceGeneratorTestHost.Run(compilation).Diagnostics);
        return SourceGeneratorTestHost.CompileImage(compilation);
    }

    private static string Handler(
        string kind,
        string name = "Handler",
        string messageType = "global::Contracts.Message")
    {
        var contract = kind == "Request"
            ? $"IRequestHandler<{messageType}, int>"
            : $"I{kind}Handler<{messageType}>";
        var returnType = kind == "Request" ? "ValueTask<int>" : "ValueTask";
        var result = kind == "Request" ? "ValueTask.FromResult(1)" : "ValueTask.CompletedTask";

        return $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            namespace Handlers
            {
                internal sealed class {{name}} : {{contract}}
                {
                    public {{returnType}} HandleAsync({{messageType}} message, CancellationToken cancellationToken)
                        => {{result}};
                }
            }
            """;
    }
}
