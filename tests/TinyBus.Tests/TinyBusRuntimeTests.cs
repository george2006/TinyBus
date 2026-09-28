using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TinyBus.Tests;

public sealed class TinyBusRuntimeTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Transport_failure_prevents_host_readiness()
    {
        var providerError = new InvalidOperationException("Transport unavailable.");
        var transport = new NativeTestTransport();
        transport.Availability = Task.FromException(providerError);
        using var host = CreateHost(transport);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

        Assert.Same(providerError, failure);
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        Assert.False(lifetime.ApplicationStarted.IsCancellationRequested);
    }

    [Fact]
    public async Task Transport_cancellation_prevents_host_readiness()
    {
        var available = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new NativeTestTransport { Availability = available.Task };
        using var host = CreateHost(transport);
        using var cancellation = new CancellationTokenSource();

        var starting = host.StartAsync(cancellation.Token);
        Assert.False(starting.IsCompleted);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);

        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        Assert.False(lifetime.ApplicationStarted.IsCancellationRequested);
    }

    [Fact]
    public async Task Successful_pipeline_execution_completes_the_delivery()
    {
        var transport = new NativeTestTransport();
        var pipeline = new RecordingPipeline();
        using var host = CreateHost(transport, pipeline, maximumConcurrentMessages: 4);
        await host.StartAsync();
        var envelope = CreateEnvelope();
        var delivery = new NativeTestDelivery(envelope);

        transport.Enqueue(delivery);

        await delivery.Completed.WaitAsync(TestTimeout);
        Assert.Same(envelope, pipeline.LastMessage);
        Assert.False(delivery.Abandoned.IsCompleted);
        Assert.False(delivery.RetryScheduled.IsCompleted);
        Assert.False(delivery.DeadLettered.IsCompleted);
        await host.StopAsync();
    }

    [Fact]
    public async Task Pipeline_failure_schedules_the_delivery_for_retry()
    {
        var transport = new NativeTestTransport();
        var pipelineError = new InvalidOperationException("Handler failed.");
        var pipeline = new FailingPipeline(pipelineError);
        using var host = CreateHost(transport, pipeline, maximumConcurrentMessages: 4);
        await host.StartAsync();
        var firstEnvelope = CreateEnvelope();
        var secondEnvelope = CreateEnvelope();
        var firstDelivery = new NativeTestDelivery(firstEnvelope);
        var secondDelivery = new NativeTestDelivery(secondEnvelope);

        transport.Enqueue(firstDelivery);
        var firstRetry = await firstDelivery.RetryScheduled.WaitAsync(TestTimeout);
        transport.Enqueue(secondDelivery);

        var secondRetry = await secondDelivery.RetryScheduled.WaitAsync(TestTimeout);
        Assert.Same(pipelineError, firstRetry.Error);
        Assert.Same(pipelineError, secondRetry.Error);
        Assert.False(firstDelivery.Completed.IsCompleted);
        Assert.False(secondDelivery.Completed.IsCompleted);
        Assert.False(firstDelivery.Abandoned.IsCompleted);
        Assert.False(secondDelivery.Abandoned.IsCompleted);
        Assert.False(firstDelivery.DeadLettered.IsCompleted);
        Assert.False(secondDelivery.DeadLettered.IsCompleted);
        await host.StopAsync();
    }

    [Fact]
    public async Task Runtime_never_executes_more_than_the_configured_capacity()
    {
        var transport = new NativeTestTransport();
        var pipeline = new BlockingPipeline();
        using var host = CreateHost(transport, pipeline, maximumConcurrentMessages: 2);
        await host.StartAsync();
        var firstEnvelope = CreateEnvelope();
        var secondEnvelope = CreateEnvelope();
        var thirdEnvelope = CreateEnvelope();
        var first = new NativeTestDelivery(firstEnvelope);
        var second = new NativeTestDelivery(secondEnvelope);
        var third = new NativeTestDelivery(thirdEnvelope);
        transport.Enqueue(first);
        transport.Enqueue(second);
        transport.Enqueue(third);

        await pipeline.WaitForExecutionAsync(TestTimeout);
        await pipeline.WaitForExecutionAsync(TestTimeout);
        var thirdStartedEarly = await pipeline.WaitForExecutionAsync(
            TimeSpan.FromMilliseconds(100));

        Assert.False(thirdStartedEarly);
        Assert.Equal(2, pipeline.MaximumObservedConcurrency);
        var expectedInitialCapacity = new ReceiveCapacity(2, 2);
        Assert.Equal(expectedInitialCapacity, transport.ReceivedCapacities[0]);

        pipeline.ReleaseOne();

        await pipeline.WaitForExecutionAsync(TestTimeout);
        var expectedAvailableCapacity = new ReceiveCapacity(2, 1);
        Assert.Contains(expectedAvailableCapacity, transport.ReceivedCapacities);

        pipeline.Release(2);

        var deliveryCompletions = new[]
        {
            first.Completed,
            second.Completed,
            third.Completed
        };
        var completions = Task.WhenAll(deliveryCompletions);
        await completions.WaitAsync(TestTimeout);
        Assert.Equal(2, pipeline.MaximumObservedConcurrency);
        await host.StopAsync();
    }

    [Fact]
    public async Task Shutdown_cancels_active_execution_and_abandons_its_delivery()
    {
        var transport = new NativeTestTransport();
        var pipeline = new BlockingPipeline();
        using var host = CreateHost(transport, pipeline, maximumConcurrentMessages: 1);
        await host.StartAsync();
        var envelope = CreateEnvelope();
        var delivery = new NativeTestDelivery(envelope);
        transport.Enqueue(delivery);
        await pipeline.WaitForExecutionAsync(TestTimeout);

        await host.StopAsync();

        await delivery.Abandoned.WaitAsync(TestTimeout);
        Assert.False(delivery.Completed.IsCompleted);
        Assert.False(delivery.RetryScheduled.IsCompleted);
        Assert.False(delivery.DeadLettered.IsCompleted);
    }

    private static IHost CreateHost(
        ITransport transport,
        IIncomingMessagePipeline? pipeline = null,
        int? maximumConcurrentMessages = null)
    {
        var settings = new HostApplicationBuilderSettings { DisableDefaults = true };
        var builder = new HostApplicationBuilder(settings);
        builder.Logging.ClearProviders();
        var service = new ServiceIdentity("payments");
        var messages = Array.Empty<MessageDescriptor>();
        var topology = new ServiceTopology(service, messages);
        var incomingPipeline = pipeline ?? new NoOpIncomingMessagePipeline();
        var maximumConcurrency = maximumConcurrentMessages ?? Environment.ProcessorCount;
        var runtimeSettings = new TinyBusRuntimeSettings(maximumConcurrency);
        var minimumRetryDelay = TimeSpan.FromSeconds(1);
        var maximumRetryDelay = TimeSpan.FromSeconds(30);
        var retryPolicy = new MessageRetryPolicy(
            5,
            minimumRetryDelay,
            maximumRetryDelay);
        var failureLogger = NullLogger<DeliveryFailureProcessor>.Instance;
        var failureProcessor = new DeliveryFailureProcessor(retryPolicy, failureLogger);
        var logger = NullLogger<TinyBusRuntime>.Instance;
        var runtime = new TinyBusRuntime(
            transport,
            topology,
            incomingPipeline,
            runtimeSettings,
            failureProcessor,
            logger);
        builder.Services.AddSingleton<IHostedService>(runtime);

        return builder.Build();
    }

    private static MessageEnvelope CreateEnvelope()
    {
        var messageId = Guid.NewGuid();
        var contract = new ContractIdentity("payments.capture", 1);
        var envelope = new MessageEnvelope(messageId, contract, "{}");

        return envelope;
    }

    private sealed class RecordingPipeline : IIncomingMessagePipeline
    {
        internal MessageEnvelope? LastMessage { get; private set; }

        public ValueTask ExecuteAsync(
            MessageEnvelope message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastMessage = message;

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingPipeline : IIncomingMessagePipeline
    {
        private readonly Exception exception;

        internal FailingPipeline(Exception exception)
        {
            this.exception = exception;
        }

        public ValueTask ExecuteAsync(
            MessageEnvelope message,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromException(exception);
        }
    }

    private sealed class BlockingPipeline : IIncomingMessagePipeline
    {
        private readonly SemaphoreSlim executions = new(0);
        private readonly SemaphoreSlim releases = new(0);
        private int activeExecutions;
        private int maximumObservedConcurrency;

        internal int MaximumObservedConcurrency => Volatile.Read(
            ref maximumObservedConcurrency);

        internal void ReleaseOne()
        {
            releases.Release();
        }

        internal void Release(int count)
        {
            releases.Release(count);
        }

        internal async Task<bool> WaitForExecutionAsync(TimeSpan timeout)
        {
            return await executions.WaitAsync(timeout);
        }

        public async ValueTask ExecuteAsync(
            MessageEnvelope message,
            CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref activeExecutions);
            RecordMaximumConcurrency(active);
            executions.Release();

            try
            {
                await releases.WaitAsync(cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref activeExecutions);
            }
        }

        private void RecordMaximumConcurrency(int active)
        {
            while (true)
            {
                var current = Volatile.Read(ref maximumObservedConcurrency);

                if (active <= current)
                {
                    return;
                }

                var previous = Interlocked.CompareExchange(
                    ref maximumObservedConcurrency,
                    active,
                    current);

                if (previous == current)
                {
                    return;
                }
            }
        }
    }
}
