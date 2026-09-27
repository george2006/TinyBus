using RabbitMQ.Client;

namespace TinyBus.RabbitMq.Tests;

public sealed class RabbitMqTopologyJournalTests : IClassFixture<RabbitMqFixture>
{
    private readonly RabbitMqFixture rabbitMq;

    public RabbitMqTopologyJournalTests(RabbitMqFixture rabbitMq)
    {
        this.rabbitMq = rabbitMq;
    }

    [Fact]
    public async Task Older_replica_cannot_remove_a_newer_command_declaration()
    {
        var scenarioId = Guid.NewGuid();
        var scenario = scenarioId.ToString("N");
        var capture = new ContractIdentity($"{scenario}.payments.capture", 1);
        var refund = new ContractIdentity($"{scenario}.payments.refund", 1);
        var newerReplica = CreateTopology("payments", capture, refund);
        var olderReplica = CreateTopology("payments", capture);
        var conflictingReplica = CreateTopology("checkout", refund);

        await using var connection = await rabbitMq.OpenConnectionAsync();
        var journal = new RabbitMqTopologyJournal(connection);

        await journal.ReconcileAsync(newerReplica);
        await journal.ReconcileAsync(olderReplica);

        Func<Task> reconcileConflict = () => ReconcileAsync(journal, conflictingReplica);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(reconcileConflict);

        Assert.Contains(refund.Name, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejected_declaration_does_not_claim_its_other_commands()
    {
        var scenarioId = Guid.NewGuid();
        var scenario = scenarioId.ToString("N");
        var capture = new ContractIdentity($"{scenario}.payments.capture", 1);
        var ship = new ContractIdentity($"{scenario}.shipping.ship", 1);
        var payments = CreateTopology("payments", capture);
        var conflictingCheckout = CreateTopology("checkout", capture, ship);
        var shipping = CreateTopology("shipping", ship);

        await using var connection = await rabbitMq.OpenConnectionAsync();
        var journal = new RabbitMqTopologyJournal(connection);

        await journal.ReconcileAsync(payments);

        Func<Task> reconcileConflict = () => ReconcileAsync(journal, conflictingCheckout);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(reconcileConflict);
        await journal.ReconcileAsync(shipping);

        Assert.Contains(capture.Name, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_claims_choose_one_persistent_owner()
    {
        var scenarioId = Guid.NewGuid();
        var scenario = scenarioId.ToString("N");
        var capture = new ContractIdentity($"{scenario}.payments.capture", 1);
        var payments = CreateTopology("payments", capture);
        var checkout = CreateTopology("checkout", capture);

        await using var paymentsConnection = await rabbitMq.OpenConnectionAsync();
        await using var checkoutConnection = await rabbitMq.OpenConnectionAsync();
        var paymentsJournal = new RabbitMqTopologyJournal(paymentsConnection);
        var checkoutJournal = new RabbitMqTopologyJournal(checkoutConnection);

        var paymentsClaim = CaptureFailureAsync(paymentsJournal, payments);
        var checkoutClaim = CaptureFailureAsync(checkoutJournal, checkout);
        var outcomes = await Task.WhenAll(paymentsClaim, checkoutClaim);

        var failures = outcomes.Where(outcome => outcome is not null);
        var failure = Assert.Single(failures);
        Assert.IsType<InvalidOperationException>(failure);

        var winningTopology = outcomes[0] is null ? payments : checkout;
        var losingTopology = outcomes[0] is null ? checkout : payments;

        await using var restartedConnection = await rabbitMq.OpenConnectionAsync();
        var restartedJournal = new RabbitMqTopologyJournal(restartedConnection);

        await restartedJournal.ReconcileAsync(winningTopology);

        Func<Task> reconcileLosingTopology = () => ReconcileAsync(restartedJournal, losingTopology);
        await Assert.ThrowsAsync<InvalidOperationException>(reconcileLosingTopology);
    }

    [Fact]
    public async Task Ownership_survives_a_broker_restart()
    {
        var scenarioId = Guid.NewGuid();
        var scenario = scenarioId.ToString("N");
        var capture = new ContractIdentity($"{scenario}.payments.capture", 1);
        var payments = CreateTopology("payments", capture);
        var checkout = CreateTopology("checkout", capture);

        await using (var connection = await rabbitMq.OpenConnectionAsync())
        {
            var journal = new RabbitMqTopologyJournal(connection);
            await journal.ReconcileAsync(payments);
        }

        await rabbitMq.RestartAsync();

        await using var restartedConnection = await rabbitMq.OpenConnectionAsync();
        var restartedJournal = new RabbitMqTopologyJournal(restartedConnection);
        Func<Task> reconcileConflict = () => ReconcileAsync(restartedJournal, checkout);

        await Assert.ThrowsAsync<InvalidOperationException>(reconcileConflict);
    }

    private static async Task<Exception?> CaptureFailureAsync(
        RabbitMqTopologyJournal journal,
        ServiceTopology topology)
    {
        try
        {
            await journal.ReconcileAsync(topology);
            return null;
        }
        catch (Exception error)
        {
            return error;
        }
    }

    private static Task ReconcileAsync(
        RabbitMqTopologyJournal journal,
        ServiceTopology topology)
    {
        var reconciliation = journal.ReconcileAsync(topology);
        var task = reconciliation.AsTask();

        return task;
    }

    private static ServiceTopology CreateTopology(
        string serviceName,
        params ContractIdentity[] commands)
    {
        var messages = new List<MessageDescriptor>();

        foreach (var contract in commands)
        {
            var descriptor = new MessageDescriptor(
                contract,
                typeof(TestCommand),
                typeof(TestCommandHandler),
                MessageKind.Command);
            messages.Add(descriptor);
        }

        var service = new ServiceIdentity(serviceName);
        var topology = new ServiceTopology(service, messages);

        return topology;
    }

    private sealed class TestCommand;

    private sealed class TestCommandHandler;
}
