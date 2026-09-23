using Microsoft.CodeAnalysis;

namespace TinyBus.SourceGen.Tests;

public sealed class HandlerTopologyDiagnosticTests
{
    [Fact]
    public void Rejects_multiple_command_handlers_for_one_message()
    {
        var run = SourceGeneratorTestHost.RunWithDiagnostics("""
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            public sealed record CapturePayment;

            public sealed class FirstHandler : ICommandHandler<CapturePayment>
            {
                public ValueTask HandleAsync(
                    CapturePayment command,
                    CancellationToken cancellationToken) => ValueTask.CompletedTask;
            }

            public sealed class SecondHandler : ICommandHandler<CapturePayment>
            {
                public ValueTask HandleAsync(
                    CapturePayment command,
                    CancellationToken cancellationToken) => ValueTask.CompletedTask;
            }
            """);

        AssertDiagnostics(run, "TBUS003", "FirstHandler", "SecondHandler");
    }

    [Fact]
    public void Rejects_multiple_request_handlers_for_one_message()
    {
        var run = SourceGeneratorTestHost.RunWithDiagnostics("""
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            public sealed record GetPayment;
            public sealed record Payment;

            public sealed class FirstHandler : IRequestHandler<GetPayment, Payment>
            {
                public ValueTask<Payment> HandleAsync(
                    GetPayment request,
                    CancellationToken cancellationToken) => ValueTask.FromResult(new Payment());
            }

            public sealed class SecondHandler : IRequestHandler<GetPayment, Payment>
            {
                public ValueTask<Payment> HandleAsync(
                    GetPayment request,
                    CancellationToken cancellationToken) => ValueTask.FromResult(new Payment());
            }
            """);

        AssertDiagnostics(run, "TBUS004", "FirstHandler", "SecondHandler");
    }

    [Fact]
    public void Rejects_one_message_with_conflicting_semantics()
    {
        var run = SourceGeneratorTestHost.RunWithDiagnostics("""
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            public sealed record PaymentMessage;

            public sealed class Handler
                : ICommandHandler<PaymentMessage>, IEventHandler<PaymentMessage>
            {
                public ValueTask HandleAsync(
                    PaymentMessage message,
                    CancellationToken cancellationToken) => ValueTask.CompletedTask;
            }
            """);

        AssertDiagnostics(run, "TBUS005", "Handler");
    }

    private static void AssertDiagnostics(
        GeneratorDriverRunResult run,
        string expectedId,
        params string[] expectedLocations)
    {
        Assert.All(run.Diagnostics, diagnostic => Assert.Equal(expectedId, diagnostic.Id));

        var locations = run.Diagnostics
            .Select(ReadLocationText)
            .OrderBy(location => location, StringComparer.Ordinal);

        Assert.Equal(expectedLocations, locations);
    }

    private static string ReadLocationText(Diagnostic diagnostic)
    {
        var tree = Assert.IsAssignableFrom<SyntaxTree>(diagnostic.Location.SourceTree);
        return tree.GetText().ToString(diagnostic.Location.SourceSpan);
    }
}
