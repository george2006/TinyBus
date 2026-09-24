namespace TinyBus.SourceGen.Tests;

public sealed class CommandExecutionTests
{
    [Fact]
    public void Executes_an_internal_handler_through_generated_registration()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using TinyBus;

            internal sealed record Capture(int Amount);

            internal sealed class CapturedPayments
            {
                public int Total { get; set; }
            }

            internal sealed class CaptureHandler : ICommandHandler<Capture>
            {
                private readonly CapturedPayments payments;

                public CaptureHandler(CapturedPayments payments)
                {
                    this.payments = payments;
                }

                public ValueTask HandleAsync(Capture command, CancellationToken cancellationToken)
                {
                    payments.Total += command.Amount;
                    return ValueTask.CompletedTask;
                }
            }

            public static class Scenario
            {
                public static int Run()
                {
                    var services = new ServiceCollection();
                    services.AddScoped<CapturedPayments>();
                    TinyBus.Generated.GeneratedTinyBusManifest.RegisterHandlers(services);
                    var options = new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true };
                    using var provider = services.BuildServiceProvider(options);
                    using var scope = provider.CreateScope();
                    var executor = new CommandExecutor(scope.ServiceProvider);
                    var command = new Capture(25);

                    var execution = executor.ExecuteAsync(command);
                    var completion = execution.GetAwaiter();
                    completion.GetResult();

                    var payments = scope.ServiceProvider.GetRequiredService<CapturedPayments>();
                    return payments.Total;
                }
            }
            """;

        var total = SourceGeneratorTestHost.Execute<int>(source);

        Assert.Equal(25, total);
    }
}
