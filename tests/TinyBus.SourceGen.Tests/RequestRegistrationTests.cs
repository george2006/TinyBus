using Microsoft.CodeAnalysis;

namespace TinyBus.SourceGen.Tests;

public sealed class RequestRegistrationTests
{
    [Fact]
    public void Resolves_an_internal_library_handler_and_returns_its_response()
    {
        const string librarySource = """
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            public sealed record GetStatus(int PaymentId);
            public sealed record Status(int PaymentId, string Value);

            public sealed class PaymentState
            {
                public string Value { get; set; } = "Unknown";
            }

            internal sealed class StatusHandler : IRequestHandler<GetStatus, Status>
            {
                private readonly PaymentState state;

                public StatusHandler(PaymentState state)
                {
                    this.state = state;
                }

                public ValueTask<Status> HandleAsync(GetStatus request, CancellationToken cancellationToken)
                {
                    var response = new Status(request.PaymentId, state.Value);
                    return ValueTask.FromResult(response);
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
            using Microsoft.Extensions.DependencyInjection;
            using TinyBus;

            public static class Scenario
            {
                public static string Run()
                {
                    var services = new ServiceCollection();
                    services.AddScoped<PaymentState>();
                    TinyBus.Generated.GeneratedTinyBusManifest.RegisterHandlers(services);
                    TinyBus.Generated.GeneratedTinyBusManifest.RegisterHandlers(services);
                    var options = new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true };
                    using var provider = services.BuildServiceProvider(options);
                    using var scope = provider.CreateScope();
                    var state = scope.ServiceProvider.GetRequiredService<PaymentState>();
                    state.Value = "Paid";
                    var handler = scope.ServiceProvider.GetRequiredService<IRequestHandler<GetStatus, Status>>();
                    var request = new GetStatus(42);

                    var execution = handler.HandleAsync(request, default);
                    var completion = execution.GetAwaiter();
                    var response = completion.GetResult();

                    return $"{response.PaymentId}|{response.Value}|{services.Count}";
                }
            }
            """);

        var response = SourceGeneratorTestHost.Execute<string>(root, library);

        Assert.Equal("42|Paid|2", response);
    }
}
