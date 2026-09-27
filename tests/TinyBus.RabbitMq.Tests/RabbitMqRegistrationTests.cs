using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace TinyBus.RabbitMq.Tests;

public sealed class RabbitMqRegistrationTests : IClassFixture<RabbitMqFixture>
{
    private readonly RabbitMqFixture rabbitMq;

    public RabbitMqRegistrationTests(RabbitMqFixture rabbitMq)
    {
        this.rabbitMq = rabbitMq;
    }

    [Fact]
    public async Task Host_is_ready_for_typed_command_sending()
    {
        const string serviceName = "payments-registration";
        var settings = new HostApplicationBuilderSettings { DisableDefaults = true };
        var builder = new HostApplicationBuilder(settings);
        builder.Logging.ClearProviders();

        void Configure(TinyBusOptions options)
        {
            options.Service(serviceName);
            options.UseRabbitMq(rabbitMq.ConnectionString);
        }

        builder.Services.AddTinyBus<TestManifest>(Configure);
        using var host = builder.Build();

        await host.StartAsync();

        var bus = host.Services.GetRequiredService<IBus>();
        var command = new TestCommand(42);
        await bus.SendAsync(command);
        await using var connection = await rabbitMq.OpenConnectionAsync();
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: false,
            publisherConfirmationTrackingEnabled: false);
        await using var channel = await connection.CreateChannelAsync(channelOptions);
        var queueName = "tinybus." + serviceName;
        var delivery = await channel.BasicGetAsync(queueName, autoAck: true);
        var receivedMessage = Assert.IsType<BasicGetResult>(delivery);
        var payload = Encoding.UTF8.GetString(receivedMessage.Body.Span);
        var receivedCommand = JsonSerializer.Deserialize<TestCommand>(payload);

        Assert.Equal(command, receivedCommand);
        await host.StopAsync();
    }

    private sealed class TestManifest : IBusManifest
    {
        private readonly IReadOnlyList<MessageDescriptor> messages;

        public TestManifest()
        {
            var contract = new ContractIdentity("payments.registration.capture", 1);
            var descriptor = new MessageDescriptor(
                contract,
                typeof(TestCommand),
                typeof(TestCommandHandler),
                MessageKind.Command);
            messages = new[] { descriptor };
        }

        public IReadOnlyList<MessageDescriptor> Messages => messages;
    }

    [BusContract("payments.registration.capture")]
    private sealed record TestCommand(int Amount);

    private sealed class TestCommandHandler;
}
