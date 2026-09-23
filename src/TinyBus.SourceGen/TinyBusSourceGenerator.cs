using System.Text;
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
        var analysis = Analyze(context.SyntaxProvider);
        var validation = Validate(analysis);
        var definitions = ExtractValidDefinitions(validation);
        var manifest = GenerateManifest(definitions);

        RegisterManifest(context, manifest);
        ReportDiagnostics(context, validation);
    }

    private static IncrementalValuesProvider<MessageHandlerAnalysis> Analyze(
        SyntaxValueProvider syntaxProvider)
    {
        return syntaxProvider.CreateSyntaxProvider(
                HandlerDiscovery.IsCandidateDeclaration,
                static (candidate, cancellationToken) =>
                    new HandlerAnalyzer().Analyze(candidate, cancellationToken))
            .SelectMany(static (definitions, _) => definitions);
    }

    private static IncrementalValuesProvider<MessageValidationResult> Validate(
        IncrementalValuesProvider<MessageHandlerAnalysis> analysis)
    {
        return analysis.Select(static (candidate, _) =>
            new MessageHandlerValidator().Validate(candidate));
    }

    private static IncrementalValuesProvider<MessageHandlerDefinition> ExtractValidDefinitions(
        IncrementalValuesProvider<MessageValidationResult> validation)
    {
        return validation
            .Where(static result => result.Definition is not null)
            .Select(static (result, _) => result.Definition!);
    }

    private static IncrementalValueProvider<(string HintName, string Source)> GenerateManifest(
        IncrementalValuesProvider<MessageHandlerDefinition> definitions)
    {
        return definitions.Collect()
            .Select(static (definitions, cancellationToken) =>
                new ManifestGeneration().Generate(definitions, cancellationToken));
    }

    private static void RegisterManifest(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<(string HintName, string Source)> manifest)
    {
        context.RegisterSourceOutput(manifest, static (output, source) =>
            output.AddSource(source.HintName, SourceText.From(source.Source, Encoding.UTF8)));
    }

    private static void ReportDiagnostics(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<MessageValidationResult> validation)
    {
        var issues = validation.SelectMany(static (result, _) => result.Issues);
        context.RegisterSourceOutput(issues.Combine(context.CompilationProvider), static (output, input) =>
            output.ReportDiagnostic(MessageDiagnosticReporter.Create(input.Right, input.Left)));
    }
}
