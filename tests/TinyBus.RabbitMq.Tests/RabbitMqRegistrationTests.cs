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
    public async Task Host_starts_after_the_service_queue_is_ready()
    {
        var scenarioId = Guid.NewGuid();
        var scenario = scenarioId.ToString("N");
        var serviceName = "payments-" + scenario;
        var settings = new HostApplicationBuilderSettings { DisableDefaults = true };
        var builder = new HostApplicationBuilder(settings);
        builder.Logging.ClearProviders();

        void Configure(TinyBusOptions options)
        {
            options.Service(serviceName);
            options.UseRabbitMq(rabbitMq.ConnectionString);
        }

        builder.Services.AddTinyBus<EmptyManifest>(Configure);
        using var host = builder.Build();

        await host.StartAsync();

        await using var connection = await rabbitMq.OpenConnectionAsync();
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: false,
            publisherConfirmationTrackingEnabled: false);
        await using var channel = await connection.CreateChannelAsync(channelOptions);
        var queueName = "tinybus." + serviceName;

        await channel.QueueDeclarePassiveAsync(queueName);
        await host.StopAsync();
    }

    private sealed class EmptyManifest : IBusManifest
    {
        public IReadOnlyList<MessageDescriptor> Messages { get; } = Array.Empty<MessageDescriptor>();
    }
}
