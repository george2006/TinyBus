using Microsoft.Extensions.DependencyInjection;

namespace TinyBus.Tests;

public sealed class CommandExecutorAllocationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Adds_no_dispatch_allocations_after_warmup(bool completesAsynchronously)
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<Command>, Handler>();
        var options = new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true };
        using var provider = services.BuildServiceProvider(options);
        using var scope = provider.CreateScope();
        var executor = new CommandExecutor(scope.ServiceProvider);
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<Command>>();
        var concreteHandler = (Handler)handler;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new Command(42);

        if (completesAsynchronously)
        {
            // Reuse one task-backed gate to isolate dispatch from handler-owned task allocations.
            concreteHandler.Completion = new ValueTask(completion.Task);
        }

        MeasureDirectCalls(handler, command);
        MeasureExecutorCalls(executor, command);

        var direct = MeasureDirectCalls(handler, command);
        var dispatched = MeasureExecutorCalls(executor, command);

        Assert.Equal(0L, direct.AllocatedBytes);
        Assert.Equal(0L, dispatched.AllocatedBytes);
        Assert.Equal(!completesAsynchronously, dispatched.Completion.IsCompleted);
        Assert.Equal(42, concreteHandler.LastValue);

        completion.SetResult();
        await direct.Completion;
        await dispatched.Completion;
    }

    private static (long AllocatedBytes, ValueTask Completion) MeasureDirectCalls(
        ICommandHandler<Command> handler,
        Command command)
    {
        var completion = default(ValueTask);
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 10_000; index++)
        {
            completion = handler.HandleAsync(command, CancellationToken.None);
        }

        var after = GC.GetAllocatedBytesForCurrentThread();
        return (after - before, completion);
    }

    private static (long AllocatedBytes, ValueTask Completion) MeasureExecutorCalls(
        CommandExecutor executor,
        Command command)
    {
        var completion = default(ValueTask);
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 10_000; index++)
        {
            completion = executor.ExecuteAsync(command);
        }

        var after = GC.GetAllocatedBytesForCurrentThread();
        return (after - before, completion);
    }

    private readonly record struct Command(int Value);

    private sealed class Handler : ICommandHandler<Command>
    {
        public Handler()
        {
        }

        public ValueTask Completion { get; set; }
        public int LastValue { get; private set; }

        public ValueTask HandleAsync(Command command, CancellationToken cancellationToken)
        {
            LastValue = command.Value;
            return Completion;
        }
    }
}
