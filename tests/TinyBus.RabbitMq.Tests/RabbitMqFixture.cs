using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace TinyBus.RabbitMq.Tests;

public sealed class RabbitMqFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer container;

    public RabbitMqFixture()
    {
        var builder = new RabbitMqBuilder("rabbitmq:4.3-alpine");
        container = builder.Build();
    }

    public string ConnectionString => container.GetConnectionString();

    public Task InitializeAsync()
    {
        return container.StartAsync();
    }

    public Task DisposeAsync()
    {
        var disposal = container.DisposeAsync();
        var task = disposal.AsTask();

        return task;
    }

    public async ValueTask<IConnection> OpenConnectionAsync()
    {
        var connectionString = container.GetConnectionString();
        var connectionUri = new Uri(connectionString);
        var connectionFactory = new ConnectionFactory
        {
            Uri = connectionUri
        };
        var connection = await connectionFactory.CreateConnectionAsync();

        return connection;
    }

    public async Task RestartAsync()
    {
        await container.StopAsync();
        await container.StartAsync();
    }
}
