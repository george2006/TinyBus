using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Npgsql;
using TinyBus;
using TinyBus.PostgreSql.Migrations;
using TinyBus.PostgreSql.Persistence;
using TinyBus.PostgreSql.Persistence.Commands;
using TinyBus.PostgreSql.Receiving;

namespace TinyBus.PostgreSql;

internal sealed class PostgreSqlTransport : ITransport
{
    private static readonly TimeSpan ReceivePollingInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ReceiveRecoveryInterval = TimeSpan.FromSeconds(1);

    private readonly PostgreSqlMigrator migrator;
    private readonly PostgreSqlTopologyReconciler topologyReconciler;
    private readonly SendCommandMessage sendCommandMessage;
    private readonly ClaimCommandMessages claimCommandMessages;
    private readonly CompleteCommandMessage completeCommandMessage;
    private readonly ScheduleCommandMessageRetry scheduleCommandMessageRetry;
    private readonly DeadLetterCommandMessage deadLetterCommandMessage;
    private readonly AbandonCommandMessage abandonCommandMessage;
    private readonly TimeSpan commandLeaseDuration;
    private readonly ILogger<PostgreSqlTransport> logger;
    private string? serviceName;
    private bool initialized;

    internal PostgreSqlTransport(
        string connectionString,
        TimeSpan commandLeaseDuration,
        ILogger<PostgreSqlTransport> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            commandLeaseDuration,
            TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(logger);

        migrator = new PostgreSqlMigrator(connectionString);
        topologyReconciler = new PostgreSqlTopologyReconciler(connectionString);
        sendCommandMessage = new SendCommandMessage(connectionString);
        claimCommandMessages = new ClaimCommandMessages(connectionString);
        completeCommandMessage = new CompleteCommandMessage(connectionString);
        scheduleCommandMessageRetry = new ScheduleCommandMessageRetry(connectionString);
        deadLetterCommandMessage = new DeadLetterCommandMessage(connectionString);
        abandonCommandMessage = new AbandonCommandMessage(connectionString);
        this.commandLeaseDuration = commandLeaseDuration;
        this.logger = logger;
    }

    public async ValueTask InitializeAsync(
        ServiceTopology topology,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ThrowIfInitialized();

        await migrator.MigrateAsync(cancellationToken);
        await topologyReconciler.ReconcileAsync(topology, cancellationToken);

        serviceName = topology.Service.Value;
        initialized = true;
    }

    public async ValueTask SendAsync(
        MessageEnvelope message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ThrowIfNotInitialized();

        var contract = message.Contract;
        var accepted = await sendCommandMessage.ExecuteAsync(
            message,
            cancellationToken);

        if (!accepted)
        {
            var error = $"Command '{contract.Name}' version {contract.Version} has no owner.";
            var exception = new InvalidOperationException(error);

            throw exception;
        }
    }

    public async ValueTask<IReadOnlyList<ITransportDelivery>> ReceiveAsync(
        ReceiveCapacity capacity,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNotInitialized();

        while (true)
        {
            IReadOnlyList<ClaimedCommandMessage> claimedMessages;

            try
            {
                claimedMessages = await claimCommandMessages.ExecuteAsync(
                    serviceName!,
                    capacity.Available,
                    commandLeaseDuration,
                    cancellationToken);
            }
            catch (NpgsqlException exception) when (
                ShouldRecoverReceive(exception, cancellationToken))
            {
                logger.LogWarning(
                    exception,
                    "TinyBus PostgreSQL receiving was interrupted and will retry.");
                await Task.Delay(ReceiveRecoveryInterval, cancellationToken);
                continue;
            }

            if (claimedMessages.Count > 0)
            {
                var deliveries = CreateDeliveries(claimedMessages);
                return deliveries;
            }

            await Task.Delay(ReceivePollingInterval, cancellationToken);
        }
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.CompletedTask;
    }

    private static bool ShouldRecoverReceive(
        NpgsqlException exception,
        CancellationToken cancellationToken)
    {
        return exception.IsTransient && !cancellationToken.IsCancellationRequested;
    }

    private IReadOnlyList<ITransportDelivery> CreateDeliveries(
        IReadOnlyList<ClaimedCommandMessage> claimedMessages)
    {
        var deliveries = new List<ITransportDelivery>(claimedMessages.Count);

        foreach (var claimedMessage in claimedMessages)
        {
            var delivery = new PostgreSqlCommandDelivery(
                claimedMessage,
                completeCommandMessage,
                scheduleCommandMessageRetry,
                deadLetterCommandMessage,
                abandonCommandMessage);
            deliveries.Add(delivery);
        }

        return deliveries;
    }

    private void ThrowIfInitialized()
    {
        if (initialized)
        {
            throw new InvalidOperationException("The PostgreSQL transport is already initialized.");
        }
    }

    private void ThrowIfNotInitialized()
    {
        if (!initialized)
        {
            throw new InvalidOperationException("The PostgreSQL transport has not been initialized.");
        }
    }
}
