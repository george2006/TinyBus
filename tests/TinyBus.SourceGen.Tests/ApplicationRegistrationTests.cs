using Microsoft.CodeAnalysis;

namespace TinyBus.SourceGen.Tests;

public sealed class ApplicationRegistrationTests
{
    [Fact]
    public void Application_entry_point_registers_its_identity_and_scoped_handlers()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using TinyBus;

            public sealed record Capture;

            internal sealed class CaptureHandler : ICommandHandler<Capture>
            {
                public ValueTask HandleAsync(Capture command, CancellationToken cancellationToken)
                {
                    return ValueTask.CompletedTask;
                }
            }

            public static class Scenario
            {
                public static bool[] Run()
                {
                    var services = new ServiceCollection();
                    var registered = services.AddTinyBus(bus =>
                    {
                        bus.Service("payments");
                    });

                    var options = new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true };
                    using var provider = services.BuildServiceProvider(options);
                    using var firstScope = provider.CreateScope();
                    using var secondScope = provider.CreateScope();
                    var topology = provider.GetRequiredService<ServiceTopology>();
                    var first = firstScope.ServiceProvider.GetRequiredService<ICommandHandler<Capture>>();
                    var repeated = firstScope.ServiceProvider.GetRequiredService<ICommandHandler<Capture>>();
                    var second = secondScope.ServiceProvider.GetRequiredService<ICommandHandler<Capture>>();

                    var returnsServices = ReferenceEquals(services, registered);
                    var configuredService = topology.Service.Value == "payments";
                    var composedManifest = topology.Messages.Count == 1;
                    var reusedWithinScope = ReferenceEquals(first, repeated);
                    var isolatedAcrossScopes = !ReferenceEquals(first, second);
                    return new[] { returnsServices, configuredService, composedManifest, reusedWithinScope, isolatedAcrossScopes };
                }
            }
            """;

        var result = SourceGeneratorTestHost.Execute<bool[]>(source);

        var expected = new[] { true, true, true, true, true };
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Root_registration_includes_internal_handlers_from_referenced_assemblies()
    {
        const string librarySource = """
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            namespace Payments;

            internal sealed record Capture;
            internal sealed class CaptureHandler : ICommandHandler<Capture>
            {
                public ValueTask HandleAsync(Capture command, CancellationToken cancellationToken)
                {
                    return ValueTask.CompletedTask;
                }
            }
            """;
        var noReferences = Array.Empty<MetadataReference>();
        var library = SourceGeneratorTestHost.CreateCompilationWithReferences("Payments", noReferences, librarySource);
        var libraryImage = SourceGeneratorTestHost.CompileImage(library);
        var libraryReference = MetadataReference.CreateFromImage(libraryImage);
        var references = new[] { libraryReference };

        const string hostSource = """
            using System;
            using System.Linq;
            using Microsoft.Extensions.DependencyInjection;
            using TinyBus;

            public static class Scenario
            {
                public static string[] Run()
                {
                    var services = new ServiceCollection();
                    services.AddTinyBus(bus => bus.Service("payments"));
                    var options = new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true };
                    using var provider = services.BuildServiceProvider(options);
                    using var scope = provider.CreateScope();
                    var topology = provider.GetRequiredService<ServiceTopology>();
                    var message = topology.Messages.Single();
                    var handlerInterface = typeof(ICommandHandler<>);
                    var handlerContract = handlerInterface.MakeGenericType(message.MessageType);
                    var handler = scope.ServiceProvider.GetRequiredService(handlerContract);
                    var handlerType = handler.GetType();
                    return new[] { topology.Service.Value, message.Contract.Name, handlerType.FullName! };
                }
            }
            """;
        var host = SourceGeneratorTestHost.CreateCompilationWithReferences("Host", references, hostSource);

        var result = SourceGeneratorTestHost.Execute<string[]>(host, libraryImage);

        var expected = new[] { "payments", "Payments.Capture", "Payments.CaptureHandler" };
        Assert.Equal(expected, result);
    }
}
