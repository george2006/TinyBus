using Microsoft.CodeAnalysis;

namespace TinyBus.SourceGen.Tests;

public sealed class IncomingMiddlewareTests
{
    [Fact]
    public void Executes_middleware_in_order_around_the_command_handler()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using TinyBus;

            internal sealed record Capture;

            internal static class ExecutionTrace
            {
                public static List<string> Entries { get; } = new();
            }

            [IncomingMiddleware(200)]
            internal sealed class SecondMiddleware : IIncomingMessageMiddleware
            {
                public async ValueTask InvokeAsync(
                    MessageEnvelope message,
                    IIncomingMessagePipelineRuntime runtime,
                    CancellationToken cancellationToken)
                {
                    ExecutionTrace.Entries.Add("second-before");
                    await runtime.NextAsync(message, cancellationToken);
                    ExecutionTrace.Entries.Add("second-after");
                }
            }

            [IncomingMiddleware(100)]
            internal sealed class FirstMiddleware : IIncomingMessageMiddleware
            {
                public async ValueTask InvokeAsync(
                    MessageEnvelope message,
                    IIncomingMessagePipelineRuntime runtime,
                    CancellationToken cancellationToken)
                {
                    ExecutionTrace.Entries.Add("first-before");
                    await runtime.NextAsync(message, cancellationToken);
                    ExecutionTrace.Entries.Add("first-after");
                }
            }

            internal sealed class CaptureHandler : ICommandHandler<Capture>
            {
                public ValueTask HandleAsync(Capture command, CancellationToken cancellationToken)
                {
                    ExecutionTrace.Entries.Add("handler");
                    return ValueTask.CompletedTask;
                }
            }

            public static class Scenario
            {
                public static string Run()
                {
                    var services = new ServiceCollection();
                    services.AddTinyBus(bus => bus.Service("payments"));

                    using var provider = services.BuildServiceProvider();
                    var pipeline = provider.GetRequiredService<IIncomingMessagePipeline>();
                    var messageId = Guid.NewGuid();
                    var contract = new ContractIdentity("Capture", 1);
                    var envelope = new MessageEnvelope(messageId, contract, "{}");

                    var execution = pipeline.ExecuteAsync(envelope);
                    var completion = execution.GetAwaiter();
                    completion.GetResult();

                    return string.Join(",", ExecutionTrace.Entries);
                }
            }
            """;

        var trace = SourceGeneratorTestHost.Execute<string>(source);

        Assert.Equal(
            "first-before,second-before,handler,second-after,first-after",
            trace);
    }

    [Fact]
    public void Middleware_can_short_circuit_command_execution()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using TinyBus;

            internal sealed record Capture;

            internal static class ExecutionResult
            {
                public static string Value { get; set; } = "not-run";
            }

            [IncomingMiddleware(100)]
            internal sealed class ShortCircuitMiddleware : IIncomingMessageMiddleware
            {
                public ValueTask InvokeAsync(
                    MessageEnvelope message,
                    IIncomingMessagePipelineRuntime runtime,
                    CancellationToken cancellationToken)
                {
                    ExecutionResult.Value = "short-circuited";
                    return ValueTask.CompletedTask;
                }
            }

            internal sealed class CaptureHandler : ICommandHandler<Capture>
            {
                public ValueTask HandleAsync(Capture command, CancellationToken cancellationToken)
                {
                    ExecutionResult.Value = "handler";
                    return ValueTask.CompletedTask;
                }
            }

            public static class Scenario
            {
                public static string Run()
                {
                    var services = new ServiceCollection();
                    services.AddTinyBus(bus => bus.Service("payments"));

                    using var provider = services.BuildServiceProvider();
                    var pipeline = provider.GetRequiredService<IIncomingMessagePipeline>();
                    var messageId = Guid.NewGuid();
                    var contract = new ContractIdentity("Capture", 1);
                    var envelope = new MessageEnvelope(messageId, contract, "{}");

                    var execution = pipeline.ExecuteAsync(envelope);
                    var completion = execution.GetAwaiter();
                    completion.GetResult();

                    return ExecutionResult.Value;
                }
            }
            """;

        var result = SourceGeneratorTestHost.Execute<string>(source);

        Assert.Equal("short-circuited", result);
    }

    [Fact]
    public void Rejects_duplicate_middleware_orders()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            [IncomingMiddleware(100)]
            internal sealed class FirstMiddleware : IIncomingMessageMiddleware
            {
                public ValueTask InvokeAsync(
                    MessageEnvelope message,
                    IIncomingMessagePipelineRuntime runtime,
                    CancellationToken cancellationToken)
                {
                    return ValueTask.CompletedTask;
                }
            }

            [IncomingMiddleware(100)]
            internal sealed class SecondMiddleware : IIncomingMessageMiddleware
            {
                public ValueTask InvokeAsync(
                    MessageEnvelope message,
                    IIncomingMessagePipelineRuntime runtime,
                    CancellationToken cancellationToken)
                {
                    return ValueTask.CompletedTask;
                }
            }
            """;

        var run = SourceGeneratorTestHost.RunWithDiagnostics(source);
        var diagnostics = run.Diagnostics
            .Where(diagnostic => diagnostic.Id == "TBUS008")
            .ToArray();

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic =>
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
        Assert.All(diagnostics, diagnostic =>
            Assert.Contains("FirstMiddleware, global::SecondMiddleware", diagnostic.GetMessage()));
        Assert.Empty(Assert.Single(run.Results).GeneratedSources);
    }

    [Fact]
    public void Rejects_a_decorated_class_without_the_middleware_contract()
    {
        const string source = """
            using TinyBus;

            [IncomingMiddleware(100)]
            internal sealed class InvalidMiddleware;
            """;

        var run = SourceGeneratorTestHost.RunWithDiagnostics(source);

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("TBUS007", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("InvalidMiddleware", diagnostic.GetMessage());
        Assert.Empty(Assert.Single(run.Results).GeneratedSources);
    }
}
