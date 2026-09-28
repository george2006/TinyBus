using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TinyBus;

internal sealed class TinyBusRuntime : BackgroundService
{
    private readonly ITransport transport;
    private readonly ServiceTopology topology;
    private readonly IIncomingMessagePipeline pipeline;
    private readonly TinyBusRuntimeSettings settings;
    private readonly ILogger<TinyBusRuntime> logger;
    private CancellationToken shutdownCancellationToken;

    public TinyBusRuntime(
        ITransport transport,
        ServiceTopology topology,
        IIncomingMessagePipeline pipeline,
        TinyBusRuntimeSettings settings,
        ILogger<TinyBusRuntime> logger)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        this.transport = transport;
        this.topology = topology;
        this.pipeline = pipeline;
        this.settings = settings;
        this.logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var initialization = transport.InitializeAsync(topology, cancellationToken);
        await initialization.ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        var starting = base.StartAsync(cancellationToken);
        await starting.ConfigureAwait(false);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        shutdownCancellationToken = cancellationToken;
        var stopping = base.StopAsync(cancellationToken);

        return stopping;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var executions = new List<Task>(settings.MaximumConcurrentMessages);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await RemoveCompletedExecutionsAsync(executions);

                if (executions.Count == settings.MaximumConcurrentMessages)
                {
                    await WaitForExecutionAsync(executions);
                    continue;
                }

                var available = settings.MaximumConcurrentMessages - executions.Count;
                var capacity = new ReceiveCapacity(
                    settings.MaximumConcurrentMessages,
                    available);
                var deliveries = await transport.ReceiveAsync(capacity, stoppingToken);

                foreach (var delivery in deliveries)
                {
                    var execution = ProcessDeliveryAsync(delivery, stoppingToken);
                    executions.Add(execution);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await WaitForAllExecutionsAsync(executions);
        }
    }

    private async Task ProcessDeliveryAsync(
        ITransportDelivery delivery,
        CancellationToken stoppingToken)
    {
        try
        {
            await pipeline.ExecuteAsync(delivery.Envelope, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await AbandonDeliveryAsync(delivery, stoppingToken);
            return;
        }
        catch (Exception exception)
        {
            LogProcessingFailure(delivery, exception);
            await FailDeliveryAsync(delivery, exception, stoppingToken);
            return;
        }

        await CompleteDeliveryAsync(delivery, stoppingToken);
    }

    private async Task CompleteDeliveryAsync(
        ITransportDelivery delivery,
        CancellationToken stoppingToken)
    {
        var settlementToken = ReadSettlementToken(stoppingToken);

        try
        {
            await delivery.CompleteAsync(settlementToken);
        }
        catch (Exception exception)
        {
            LogSettlementFailure(delivery, "complete", exception);
        }
    }

    private async Task FailDeliveryAsync(
        ITransportDelivery delivery,
        Exception error,
        CancellationToken stoppingToken)
    {
        var settlementToken = ReadSettlementToken(stoppingToken);

        try
        {
            await delivery.FailAsync(error, settlementToken);
        }
        catch (Exception exception)
        {
            LogSettlementFailure(delivery, "fail", exception);
        }
    }

    private async Task AbandonDeliveryAsync(
        ITransportDelivery delivery,
        CancellationToken stoppingToken)
    {
        var settlementToken = ReadSettlementToken(stoppingToken);

        try
        {
            await delivery.AbandonAsync(settlementToken);
        }
        catch (Exception exception)
        {
            LogSettlementFailure(delivery, "abandon", exception);
        }
    }

    private CancellationToken ReadSettlementToken(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return shutdownCancellationToken;
        }

        return stoppingToken;
    }

    private static async Task RemoveCompletedExecutionsAsync(List<Task> executions)
    {
        for (var index = executions.Count - 1; index >= 0; index--)
        {
            var execution = executions[index];

            if (!execution.IsCompleted)
            {
                continue;
            }

            executions.RemoveAt(index);
            await execution;
        }
    }

    private static async Task WaitForExecutionAsync(List<Task> executions)
    {
        var completed = await Task.WhenAny(executions);
        await completed;
    }

    private static async Task WaitForAllExecutionsAsync(List<Task> executions)
    {
        foreach (var execution in executions)
        {
            await execution;
        }
    }

    private void LogProcessingFailure(
        ITransportDelivery delivery,
        Exception exception)
    {
        var messageId = delivery.Envelope.MessageId;

        logger.LogError(
            exception,
            "TinyBus message {MessageId} failed during pipeline execution.",
            messageId);
    }

    private void LogSettlementFailure(
        ITransportDelivery delivery,
        string operation,
        Exception exception)
    {
        var messageId = delivery.Envelope.MessageId;

        logger.LogError(
            exception,
            "TinyBus message {MessageId} could not {SettlementOperation} its transport delivery.",
            messageId,
            operation);
    }
}
