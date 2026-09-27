using System.Text.Json;

namespace TinyBus.Tests;

public sealed class BusTests
{
    [Fact]
    public async Task Send_prepares_the_command_for_the_transport()
    {
        var transport = new NativeTestTransport();
        var bus = new Bus(transport);
        var command = new CapturePayment(42);

        await bus.SendAsync(command);

        var message = Assert.IsType<MessageEnvelope>(transport.SentMessage);
        var deserialized = JsonSerializer.Deserialize<CapturePayment>(message.Payload);
        var expectedContract = new ContractIdentity("payments.capture", 2);

        Assert.NotEqual(Guid.Empty, message.MessageId);
        Assert.Equal(expectedContract, message.Contract);
        Assert.Equal(command, deserialized);
        Assert.Null(message.CorrelationId);
        Assert.Null(message.CausationId);
        Assert.Null(message.Headers);
    }

    [Fact]
    public async Task Send_uses_the_fully_qualified_name_by_convention()
    {
        var transport = new NativeTestTransport();
        var bus = new Bus(transport);
        var command = new ConventionalCommand();

        await bus.SendAsync(command);

        var message = Assert.IsType<MessageEnvelope>(transport.SentMessage);

        Assert.Equal("TinyBus.Tests.BusTests.ConventionalCommand", message.Contract.Name);
        Assert.Equal(1, message.Contract.Version);
    }

    [Fact]
    public async Task Send_honors_cancellation_before_preparing_the_command()
    {
        var transport = new NativeTestTransport();
        var bus = new Bus(transport);
        var command = new ConventionalCommand();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Task Send()
        {
            var sending = bus.SendAsync(command, cancellation.Token);
            var task = sending.AsTask();

            return task;
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(Send);

        Assert.Null(transport.SentMessage);
    }

    [Fact]
    public async Task Generic_commands_require_an_explicit_contract_name()
    {
        var transport = new NativeTestTransport();
        var bus = new Bus(transport);
        var command = new GenericCommand<int>(42);

        Task Send()
        {
            var sending = bus.SendAsync(command);
            var task = sending.AsTask();

            return task;
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(Send);

        Assert.Contains("requires BusContractAttribute", exception.Message);
        Assert.Null(transport.SentMessage);
    }

    [BusContract("payments.capture", Version = 2)]
    private sealed record CapturePayment(int Amount);

    private sealed record ConventionalCommand;

    private sealed record GenericCommand<T>(T Value);
}
