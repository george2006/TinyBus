using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using TinyBus.SourceGen.Analysis;
using TinyBus.SourceGen.Diagnostics;
using TinyBus.SourceGen.Discovery;
using TinyBus.SourceGen.Generation;
using TinyBus.SourceGen.Model;
using TinyBus.SourceGen.Validation;

namespace TinyBus.SourceGen;

[Generator(LanguageNames.CSharp)]
public sealed class TinyBusSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var handlers = FindHandlers(context);
        context.RegisterSourceOutput(handlers, BuildManifest);
    }

    private static void BuildManifest(
        SourceProductionContext output,
        (Compilation Compilation, ImmutableArray<MessageHandlerAnalysis> Handlers) input)
    {
        var cancellationToken = output.CancellationToken;
        var analysis = Analyze(input.Compilation, input.Handlers, cancellationToken);

        var validation = Validate(
            analysis.AssemblyName, analysis.Handlers, analysis.Contributions, cancellationToken);
        var hasErrors = ReportDiagnostics(output, input.Compilation, validation.Issues);

        if (hasErrors)
        {
            return;
        }

        var manifest = Generate(
            analysis.AssemblyName, validation.Definitions, analysis.Contributions, cancellationToken);
        WriteManifest(output, manifest);
    }

    private static (
        string AssemblyName,
        ImmutableArray<MessageHandlerAnalysis> Handlers,
        ImmutableArray<ReferencedMessageContribution> Contributions) Analyze(
        Compilation compilation,
        ImmutableArray<MessageHandlerAnalysis> handlers,
        CancellationToken cancellationToken)
    {
        var assemblyName = compilation.AssemblyName ?? "Assembly";
        var contributionAnalyzer = new ReferencedContributionAnalyzer();
        var contributions = contributionAnalyzer.Analyze(compilation, cancellationToken);

        return (assemblyName, handlers, contributions);
    }

    private static (
        ImmutableArray<MessageHandlerDefinition> Definitions,
        ImmutableArray<MessageIssue> Issues) Validate(
        string assemblyName,
        ImmutableArray<MessageHandlerAnalysis> analysis,
        ImmutableArray<ReferencedMessageContribution> contributions,
        CancellationToken cancellationToken)
    {
        var handlers = ValidateHandlers(analysis, cancellationToken);

        var topologyValidator = new TopologyValidator();
        var topologyIssues = topologyValidator.Validate(
            assemblyName, handlers, contributions, cancellationToken);

        var definitions = ExtractValidDefinitions(handlers);
        var issues = CombineValidationIssues(handlers, topologyIssues);

        return (definitions, issues);
    }

    private static (string HintName, string Source) Generate(
        string assemblyName,
        ImmutableArray<MessageHandlerDefinition> definitions,
        ImmutableArray<ReferencedMessageContribution> contributions,
        CancellationToken cancellationToken)
    {
        var generation = new ManifestGeneration();
        return generation.Generate(assemblyName, definitions, contributions, cancellationToken);
    }

    private static void WriteManifest(
        SourceProductionContext output,
        (string HintName, string Source) manifest)
    {
        var source = SourceText.From(manifest.Source, Encoding.UTF8);
        output.AddSource(manifest.HintName, source);
    }

    private static ImmutableArray<MessageValidationResult> ValidateHandlers(
        ImmutableArray<MessageHandlerAnalysis> analysis,
        CancellationToken cancellationToken)
    {
        var validator = new MessageHandlerValidator();
        var results = ImmutableArray.CreateBuilder<MessageValidationResult>(analysis.Length);

        foreach (var candidate in analysis)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = validator.Validate(candidate);
            results.Add(result);
        }

        return results.ToImmutable();
    }

    private static ImmutableArray<MessageHandlerDefinition> ExtractValidDefinitions(
        ImmutableArray<MessageValidationResult> validation)
    {
        return validation
            .Where(result => result.Definition is not null)
            .Select(result => result.Definition!)
            .ToImmutableArray();
    }

    private static ImmutableArray<MessageIssue> CombineValidationIssues(
        ImmutableArray<MessageValidationResult> handlers,
        ImmutableArray<MessageIssue> topologyIssues)
    {
        return handlers.SelectMany(result => result.Issues)
            .Concat(topologyIssues)
            .ToImmutableArray();
    }

    private static bool ReportDiagnostics(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<MessageIssue> issues)
    {
        var hasErrors = false;

        foreach (var issue in issues)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var diagnostic = MessageDiagnosticReporter.Create(compilation, issue);
            context.ReportDiagnostic(diagnostic);
            hasErrors |= diagnostic.Severity == DiagnosticSeverity.Error;
        }

        return hasErrors;
    }

    private static IncrementalValueProvider<(
        Compilation Compilation,
        ImmutableArray<MessageHandlerAnalysis> Handlers)> FindHandlers(
        IncrementalGeneratorInitializationContext context)
    {
        var handlers = context.SyntaxProvider.CreateSyntaxProvider(
            HandlerDiscovery.IsCandidateDeclaration,
            static (candidate, cancellationToken) =>
            {
                var analyzer = new HandlerAnalyzer();
                return analyzer.Analyze(candidate, cancellationToken);
            })
            .SelectMany(static (candidates, _) => candidates);

        var collectedHandlers = handlers.Collect();
        return context.CompilationProvider.Combine(collectedHandlers)
            .Select(static (input, _) => (Compilation: input.Left, Handlers: input.Right));
    }
}
