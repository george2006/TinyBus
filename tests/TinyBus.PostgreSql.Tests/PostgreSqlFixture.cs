using Testcontainers.PostgreSql;

namespace TinyBus.PostgreSql.Tests;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container;

    public PostgreSqlFixture()
    {
        var builder = new PostgreSqlBuilder("postgres:17-alpine");
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
}
