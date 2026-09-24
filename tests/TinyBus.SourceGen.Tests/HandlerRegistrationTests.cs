using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TinyBus.SourceGen.Tests;

public sealed class HandlerRegistrationTests
{
    [Fact]
    public void Resolves_scoped_command_handlers_and_their_dependencies()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using TinyBus;

            public sealed record Capture;
            public sealed class Dependency;

            internal sealed class CaptureHandler : ICommandHandler<Capture>, IDisposable
            {
                public CaptureHandler(Dependency dependency)
                {
                    Dependency = dependency;
                }

                public Dependency Dependency { get; }
                public bool Disposed { get; private set; }

                public ValueTask HandleAsync(Capture command, CancellationToken cancellationToken)
                {
                    return ValueTask.CompletedTask;
                }

                public void Dispose()
                {
                    Disposed = true;
                }
            }

            public static class Scenario
            {
                public static bool[] Run()
                {
                    var services = new ServiceCollection();
                    services.AddScoped<Dependency>();
                    TinyBus.Generated.GeneratedTinyBusManifest.RegisterHandlers(services);
                    var options = new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true };
                    using var provider = services.BuildServiceProvider(options);
                    var firstScope = provider.CreateScope();
                    using var secondScope = provider.CreateScope();

                    var first = firstScope.ServiceProvider.GetRequiredService<ICommandHandler<Capture>>();
                    var repeated = firstScope.ServiceProvider.GetRequiredService<ICommandHandler<Capture>>();
                    var second = secondScope.ServiceProvider.GetRequiredService<ICommandHandler<Capture>>();
                    var dependency = firstScope.ServiceProvider.GetRequiredService<Dependency>();
                    var handler = (CaptureHandler)first;

                    var reusedWithinScope = ReferenceEquals(first, repeated);
                    var isolatedBetweenScopes = !ReferenceEquals(first, second);
                    var receivedScopedDependency = ReferenceEquals(dependency, handler.Dependency);
                    firstScope.Dispose();

                    return new[] { reusedWithinScope, isolatedBetweenScopes, receivedScopedDependency, handler.Disposed };
                }
            }
            """;

        var result = SourceGeneratorTestHost.Execute<bool[]>(source);

        var expected = new[] { true, true, true, true };
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Keeps_all_event_handlers_when_registration_is_repeated()
    {
        const string source = """
            using System.Linq;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using TinyBus;

            public sealed record Changed;

            internal sealed class FirstHandler : IEventHandler<Changed>
            {
                public ValueTask HandleAsync(Changed message, CancellationToken cancellationToken)
                {
                    return ValueTask.CompletedTask;
                }
            }

            internal sealed class SecondHandler : IEventHandler<Changed>
            {
                public ValueTask HandleAsync(Changed message, CancellationToken cancellationToken)
                {
                    return ValueTask.CompletedTask;
                }
            }

            public static class Scenario
            {
                public static bool[] Run()
                {
                    var services = new ServiceCollection();
                    services.AddScoped<IEventHandler<Changed>, FirstHandler>();
                    TinyBus.Generated.GeneratedTinyBusManifest.RegisterHandlers(services);
                    TinyBus.Generated.GeneratedTinyBusManifest.RegisterHandlers(services);
                    var options = new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true };
                    using var provider = services.BuildServiceProvider(options);
                    using var scope = provider.CreateScope();
                    using var otherScope = provider.CreateScope();
                    var firstResolution = scope.ServiceProvider.GetServices<IEventHandler<Changed>>();
                    var repeatedResolution = scope.ServiceProvider.GetServices<IEventHandler<Changed>>();
                    var otherResolution = otherScope.ServiceProvider.GetServices<IEventHandler<Changed>>();
                    var handlers = firstResolution.ToArray();
                    var repeated = repeatedResolution.ToArray();
                    var other = otherResolution.ToArray();

                    var allHandlersRegisteredOnce = services.Count == 2 && handlers.Length == 2;
                    var retainedFirstHandler = handlers[0] is FirstHandler;
                    var retainedSecondHandler = handlers[1] is SecondHandler;
                    var firstIsReused = ReferenceEquals(handlers[0], repeated[0]);
                    var secondIsReused = ReferenceEquals(handlers[1], repeated[1]);
                    var firstIsIsolated = !ReferenceEquals(handlers[0], other[0]);
                    var secondIsIsolated = !ReferenceEquals(handlers[1], other[1]);

                    return new[]
                    {
                        allHandlersRegisteredOnce, retainedFirstHandler, retainedSecondHandler,
                        firstIsReused, secondIsReused, firstIsIsolated, secondIsIsolated
                    };
                }
            }
            """;

        var result = SourceGeneratorTestHost.Execute<bool[]>(source);

        var expected = new[] { true, true, true, true, true, true, true };
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Resolves_internal_library_handlers_through_composed_registrations()
    {
        var contracts = CompileLibrary("Contracts", "public sealed record Changed;");
        var firstHandlers = LibraryHandlers("First");
        var first = CompileLibrary("First", firstHandlers, contracts);
        var secondHandlers = LibraryHandlers("Second");
        var second = CompileLibrary("Second", secondHandlers, contracts, first);
        var references = new[] { contracts, first, second };
        var root = CreateCompilation("Root", references, """
            using System.Linq;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using TinyBus;

            internal sealed class RootHandler : IEventHandler<Changed>
            {
                public ValueTask HandleAsync(Changed message, CancellationToken cancellationToken)
                {
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

                    var commandRegistrations = services.Where(registration =>
                        registration.ServiceType != typeof(IEventHandler<Changed>));
                    var commandNames = commandRegistrations.Select(registration =>
                    {
                        var instance = scope.ServiceProvider.GetRequiredService(registration.ServiceType);
                        var type = instance.GetType();
                        return type.FullName!;
                    });
                    var eventHandlers = scope.ServiceProvider.GetServices<IEventHandler<Changed>>();
                    var eventNames = eventHandlers.Select(handler =>
                    {
                        var type = handler.GetType();
                        return type.FullName!;
                    });

                    var names = commandNames.Concat(eventNames);
                    return names.ToArray();
                }
            }
            """);

        var result = SourceGeneratorTestHost.Execute<string[]>(root, contracts, first, second);

        var expected = new[]
        {
            "First.CommandHandler", "Second.CommandHandler",
            "RootHandler", "First.EventHandler", "Second.EventHandler"
        };
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Empty_assemblies_have_an_empty_registration_method()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;

            public static class Scenario
            {
                public static int Run()
                {
                    var services = new ServiceCollection();
                    TinyBus.Generated.GeneratedTinyBusManifest.RegisterHandlers(services);
                    TinyBus.Generated.GeneratedTinyBusManifest.RegisterHandlers(services);
                    return services.Count;
                }
            }
            """;

        var result = SourceGeneratorTestHost.Execute<int>(source);

        Assert.Equal(0, result);
    }

    [Fact]
    public void Registers_scoped_request_handlers_and_preserves_their_metadata()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using TinyBus;

            public sealed record GetStatus;
            public sealed record Status;

            internal sealed class StatusHandler : IRequestHandler<GetStatus, Status>
            {
                public ValueTask<Status> HandleAsync(GetStatus request, CancellationToken cancellationToken)
                {
                    var response = new Status();
                    return ValueTask.FromResult(response);
                }
            }

            public static class Scenario
            {
                public static bool Run()
                {
                    var services = new ServiceCollection();
                    TinyBus.Generated.GeneratedTinyBusManifest.RegisterHandlers(services);
                    TinyBus.Generated.GeneratedTinyBusManifest.RegisterHandlers(services);
                    var options = new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true };
                    using var provider = services.BuildServiceProvider(options);
                    using var scope = provider.CreateScope();
                    using var otherScope = provider.CreateScope();
                    var handler = scope.ServiceProvider.GetRequiredService<IRequestHandler<GetStatus, Status>>();
                    var repeatedHandler = scope.ServiceProvider.GetRequiredService<IRequestHandler<GetStatus, Status>>();
                    var otherHandler = otherScope.ServiceProvider.GetRequiredService<IRequestHandler<GetStatus, Status>>();
                    var manifest = new TinyBus.Generated.GeneratedTinyBusManifest();
                    var requestMetadataPreserved = manifest.Messages.Count == 1
                        && manifest.Messages[0].Kind == MessageKind.Request;
                    var reusedWithinScope = ReferenceEquals(handler, repeatedHandler);
                    var isolatedBetweenScopes = !ReferenceEquals(handler, otherHandler);

                    return requestMetadataPreserved && services.Count == 1
                        && reusedWithinScope && isolatedBetweenScopes;
                }
            }
            """;

        var result = SourceGeneratorTestHost.Execute<bool>(source);

        Assert.True(result);
    }

    private static string LibraryHandlers(string assemblyName)
    {
        return $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            namespace {{assemblyName}};

            internal sealed record Command;

            internal sealed class CommandHandler : ICommandHandler<Command>
            {
                public ValueTask HandleAsync(Command command, CancellationToken cancellationToken)
                {
                    return ValueTask.CompletedTask;
                }
            }

            internal sealed class EventHandler : IEventHandler<Changed>
            {
                public ValueTask HandleAsync(Changed message, CancellationToken cancellationToken)
                {
                    return ValueTask.CompletedTask;
                }
            }
            """;
    }

    private static byte[] CompileLibrary(string assemblyName, string source, params byte[][] references)
    {
        var compilation = CreateCompilation(assemblyName, references, source);
        return SourceGeneratorTestHost.CompileImage(compilation);
    }

    private static CSharpCompilation CreateCompilation(string assemblyName, byte[][] references, string source)
    {
        var metadataReferences = references.Select(image => MetadataReference.CreateFromImage(image));
        return SourceGeneratorTestHost.CreateCompilationWithReferences(assemblyName, metadataReferences, source);
    }
}
