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
        var manifestIssues = ValidateManifest(validation);
        var assemblyName = ReadAssemblyName(context.CompilationProvider);
        var manifest = GenerateManifest(assemblyName, definitions);

        RegisterManifest(context, manifest);
        ReportDiagnostics(context, validation, manifestIssues);
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

    private static IncrementalValuesProvider<MessageIssue> ValidateManifest(
        IncrementalValuesProvider<MessageValidationResult> validation)
    {
        return validation.Collect()
            .Select(static (results, cancellationToken) =>
                new ManifestValidator().Validate(results, cancellationToken))
            .SelectMany(static (issues, _) => issues);
    }

    private static IncrementalValueProvider<(string HintName, string Source)> GenerateManifest(
        IncrementalValueProvider<string> assemblyName,
        IncrementalValuesProvider<MessageHandlerDefinition> definitions)
    {
        return assemblyName.Combine(definitions.Collect())
            .Select(static (input, cancellationToken) =>
                new ManifestGeneration().Generate(input.Left, input.Right, cancellationToken));
    }

    private static IncrementalValueProvider<string> ReadAssemblyName(
        IncrementalValueProvider<Compilation> compilation)
    {
        return compilation.Select(static (value, _) => value.AssemblyName ?? "Assembly");
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
        IncrementalValuesProvider<MessageValidationResult> validation,
        IncrementalValuesProvider<MessageIssue> manifestIssues)
    {
        var contractIssues = validation.SelectMany(static (result, _) => result.Issues);

        RegisterDiagnostics(context, contractIssues);
        RegisterDiagnostics(context, manifestIssues);
    }

    private static void RegisterDiagnostics(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<MessageIssue> issues)
    {
        context.RegisterSourceOutput(issues.Combine(context.CompilationProvider), static (output, input) =>
            output.ReportDiagnostic(MessageDiagnosticReporter.Create(input.Right, input.Left)));
    }
}
