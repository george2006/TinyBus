using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using TinyBus.SourceGen.Analysis;
using TinyBus.SourceGen.Discovery;
using TinyBus.SourceGen.Generation;
using TinyBus.SourceGen.Model;

namespace TinyBus.SourceGen;

[Generator(LanguageNames.CSharp)]
public sealed class TinyBusSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var handlers = Analyze(context.SyntaxProvider);
        var manifest = handlers.Collect()
            .Select(static (definitions, cancellationToken) =>
                new ManifestGeneration().Generate(definitions, cancellationToken));

        context.RegisterSourceOutput(manifest, static (output, source) =>
            output.AddSource(source.HintName, SourceText.From(source.Source, Encoding.UTF8)));
    }

    private static IncrementalValuesProvider<MessageHandlerDefinition> Analyze(
        SyntaxValueProvider syntaxProvider)
    {
        return syntaxProvider.CreateSyntaxProvider(
                HandlerDiscovery.IsCandidateDeclaration,
                static (candidate, cancellationToken) =>
                    new HandlerAnalyzer().Analyze(candidate, cancellationToken))
            .SelectMany(static (definitions, _) => definitions);
    }
}
