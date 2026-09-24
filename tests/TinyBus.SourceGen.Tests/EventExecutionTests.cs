using Microsoft.CodeAnalysis;

namespace TinyBus.SourceGen.Tests;

public sealed class EventExecutionTests
{
    [Fact]
    public void Executes_all_local_and_internal_library_handlers()
    {
        const string librarySource = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            public sealed record Changed(List<string> Handlers);

            internal sealed class LibraryHandler : IEventHandler<Changed>
            {
                public ValueTask HandleAsync(Changed message, CancellationToken cancellationToken)
                {
                    message.Handlers.Add("library");
                    return ValueTask.CompletedTask;
                }
            }
            """;

        var noReferences = Array.Empty<MetadataReference>();
        var libraryCompilation = SourceGeneratorTestHost.CreateCompilationWithReferences(
            "Library", noReferences, librarySource);
        var library = SourceGeneratorTestHost.CompileImage(libraryCompilation);
        var libraryReference = MetadataReference.CreateFromImage(library);
        var references = new[] { libraryReference };
        var root = SourceGeneratorTestHost.CreateCompilationWithReferences("Root", references, """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using TinyBus;

            internal sealed class LocalHandler : IEventHandler<Changed>
            {
                public ValueTask HandleAsync(Changed message, CancellationToken cancellationToken)
                {
                    message.Handlers.Add("local");
                    return ValueTask.CompletedTask;
                }
            }

            internal sealed class SecondLocalHandler : IEventHandler<Changed>
            {
                public ValueTask HandleAsync(Changed message, CancellationToken cancellationToken)
                {
                    message.Handlers.Add("second local");
                    return ValueTask.CompletedTask;
                }
            }

            public static class Scenario
            {
                public static string[] Run()
                {
                    var services = new ServiceCollection();
                    TinyBus.Generated.GeneratedTinyBusManifest.RegisterHandlers(services);
                    TinyBus.Generated.GeneratedTinyBusManifest.RegisterHandlers(services);
                    var options = new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true };
                    using var provider = services.BuildServiceProvider(options);
                    using var scope = provider.CreateScope();
                    var executor = new EventExecutor(scope.ServiceProvider);
                    var handlers = new List<string>();
                    var message = new Changed(handlers);

                    var execution = executor.ExecuteAsync(message);
                    var completion = execution.GetAwaiter();
                    completion.GetResult();

                    return handlers.ToArray();
                }
            }
            """);

        var executed = SourceGeneratorTestHost.Execute<string[]>(root, library);

        var expected = new[] { "local", "second local", "library" };
        Assert.Equal(expected, executed);
    }
}
