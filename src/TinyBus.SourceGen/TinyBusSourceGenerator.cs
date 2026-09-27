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
        var middleware = FindMiddleware(context);
        var inputs = CombineInputs(context, handlers, middleware);

        context.RegisterSourceOutput(inputs, BuildSources);
    }

    private static void BuildSources(
        SourceProductionContext output,
        GeneratorInput input)
    {
        var cancellationToken = output.CancellationToken;
        var analysis = Analyze(input, cancellationToken);
        var validation = Validate(analysis, cancellationToken);
        var hasErrors = ReportDiagnostics(
            output,
            input.Compilation,
            validation.MessageIssues,
            validation.MiddlewareIssues);

        if (hasErrors)
        {
            return;
        }

        var sources = Generate(analysis, validation, cancellationToken);
        WriteSources(output, sources);
    }

    private static GeneratorAnalysis Analyze(
        GeneratorInput input,
        CancellationToken cancellationToken)
    {
        var assemblyName = input.Compilation.AssemblyName ?? "Assembly";
        var contributionAnalyzer = new ReferencedContributionAnalyzer();
        var contributions = contributionAnalyzer.Analyze(
            input.Compilation,
            cancellationToken);
        var analysis = new GeneratorAnalysis(
            assemblyName,
            input.Handlers,
            input.Middleware,
            contributions);

        return analysis;
    }

    private static GeneratorValidation Validate(
        GeneratorAnalysis analysis,
        CancellationToken cancellationToken)
    {
        var handlers = ValidateHandlers(analysis.Handlers, cancellationToken);

        var topologyValidator = new TopologyValidator();
        var topologyIssues = topologyValidator.Validate(
            analysis.AssemblyName,
            handlers,
            analysis.Contributions,
            cancellationToken);

        var messageDefinitions = ExtractValidDefinitions(handlers);
        var messageIssues = CombineValidationIssues(handlers, topologyIssues);

        var middlewareValidator = new MiddlewareValidator();
        var middleware = middlewareValidator.Validate(
            analysis.Middleware,
            cancellationToken);
        var validation = new GeneratorValidation(
            messageDefinitions,
            middleware.Definitions,
            messageIssues,
            middleware.Issues);

        return validation;
    }

    private static ImmutableArray<(string HintName, string Source)> Generate(
        GeneratorAnalysis analysis,
        GeneratorValidation validation,
        CancellationToken cancellationToken)
    {
        var generation = new SourceGeneration();
        var sources = generation.Generate(
            analysis.AssemblyName,
            validation.Messages,
            validation.Middleware,
            analysis.Contributions,
            cancellationToken);

        return sources;
    }

    private static void WriteSources(
        SourceProductionContext output,
        ImmutableArray<(string HintName, string Source)> sources)
    {
        foreach (var generated in sources)
        {
            output.CancellationToken.ThrowIfCancellationRequested();
            var source = SourceText.From(generated.Source, Encoding.UTF8);
            output.AddSource(generated.HintName, source);
        }
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
        ImmutableArray<MessageIssue> messageIssues,
        ImmutableArray<MiddlewareIssue> middlewareIssues)
    {
        var hasMessageErrors = ReportMessageDiagnostics(
            context,
            compilation,
            messageIssues);
        var hasMiddlewareErrors = ReportMiddlewareDiagnostics(
            context,
            compilation,
            middlewareIssues);

        return hasMessageErrors || hasMiddlewareErrors;
    }

    private static bool ReportMessageDiagnostics(
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

    private static bool ReportMiddlewareDiagnostics(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<MiddlewareIssue> issues)
    {
        var hasErrors = false;

        foreach (var issue in issues)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var diagnostic = MiddlewareDiagnosticReporter.Create(compilation, issue);
            context.ReportDiagnostic(diagnostic);
            hasErrors |= diagnostic.Severity == DiagnosticSeverity.Error;
        }

        return hasErrors;
    }

    private static IncrementalValuesProvider<MessageHandlerAnalysis> FindHandlers(
        IncrementalGeneratorInitializationContext context)
    {
        return context.SyntaxProvider.CreateSyntaxProvider(
            HandlerDiscovery.IsCandidateDeclaration,
            static (candidate, cancellationToken) =>
            {
                var analyzer = new HandlerAnalyzer();
                return analyzer.Analyze(candidate, cancellationToken);
            })
            .SelectMany(static (candidates, _) => candidates);
    }

    private static IncrementalValuesProvider<MiddlewareAnalysis> FindMiddleware(
        IncrementalGeneratorInitializationContext context)
    {
        return context.SyntaxProvider.CreateSyntaxProvider(
                MiddlewareDiscovery.IsCandidateDeclaration,
                static (candidate, cancellationToken) =>
                {
                    var analyzer = new MiddlewareAnalyzer();
                    return analyzer.Analyze(candidate, cancellationToken);
                })
            .Where(static candidate => candidate is not null)
            .Select(static (candidate, _) => candidate!);
    }

    private static IncrementalValueProvider<GeneratorInput> CombineInputs(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<MessageHandlerAnalysis> handlers,
        IncrementalValuesProvider<MiddlewareAnalysis> middleware)
    {
        var declarations = handlers.Collect().Combine(middleware.Collect());

        return context.CompilationProvider.Combine(declarations)
            .Select(static (input, _) =>
            {
                var compilation = input.Left;
                var handlers = input.Right.Left;
                var middleware = input.Right.Right;
                var generatorInput = new GeneratorInput(
                    compilation,
                    handlers,
                    middleware);

                return generatorInput;
            });
    }
}
