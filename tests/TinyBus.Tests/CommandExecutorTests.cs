using Microsoft.Extensions.DependencyInjection;

namespace TinyBus.Tests;

public sealed class CommandExecutorTests
{
    [Fact]
    public async Task Forwards_the_command_and_token_to_the_scoped_handler()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        using var otherScope = provider.CreateScope();
        using var cancellation = new CancellationTokenSource();
        var executor = new CommandExecutor(scope.ServiceProvider);
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<Command>>();
        var recordingHandler = (RecordingHandler)handler;
        var otherHandler = otherScope.ServiceProvider.GetRequiredService<ICommandHandler<Command>>();
        var otherRecordingHandler = (RecordingHandler)otherHandler;
        var command = new Command();
        var token = cancellation.Token;

        await executor.ExecuteAsync(command, token);
        await executor.ExecuteAsync(command, token);

        Assert.Same(command, recordingHandler.Command);
        Assert.Equal(token, recordingHandler.Token);
        Assert.Equal(2, recordingHandler.CallCount);
        Assert.Equal(0, otherRecordingHandler.CallCount);
    }

    [Fact]
    public async Task Preserves_pending_completion()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var executor = new CommandExecutor(scope.ServiceProvider);
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<Command>>();
        var recordingHandler = (RecordingHandler)handler;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        recordingHandler.Completion = new ValueTask(completion.Task);
        var command = new Command();

        var execution = executor.ExecuteAsync(command);

        Assert.False(execution.IsCompleted);
        completion.SetResult();
        await execution;
    }

    [Fact]
    public void Preserves_a_synchronous_handler_failure()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var executor = new CommandExecutor(scope.ServiceProvider);
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<Command>>();
        var recordingHandler = (RecordingHandler)handler;
        var failure = new InvalidOperationException("Handler failed.");
        recordingHandler.Failure = failure;
        var command = new Command();

        var observed = Assert.Throws<InvalidOperationException>(() =>
        {
            executor.ExecuteAsync(command);
        });

        Assert.Same(failure, observed);
    }

    [Fact]
    public async Task Preserves_an_asynchronous_handler_failure()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var executor = new CommandExecutor(scope.ServiceProvider);
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<Command>>();
        var recordingHandler = (RecordingHandler)handler;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        recordingHandler.Completion = new ValueTask(completion.Task);
        var failure = new InvalidOperationException("Handler failed asynchronously.");
        var command = new Command();

        var execution = executor.ExecuteAsync(command);
        Assert.False(execution.IsCompleted);
        completion.SetException(failure);

        var observed = await Assert.ThrowsAsync<InvalidOperationException>(async () => await execution);

        Assert.Same(failure, observed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preserves_handler_cancellation(bool cancelBeforeExecution)
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        using var cancellation = new CancellationTokenSource();
        var executor = new CommandExecutor(scope.ServiceProvider);
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<Command>>();
        var recordingHandler = (RecordingHandler)handler;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        recordingHandler.Completion = new ValueTask(completion.Task);
        var command = new Command();
        var token = cancellation.Token;

        if (cancelBeforeExecution)
        {
            cancellation.Cancel();
            completion.SetCanceled(token);
        }

        var execution = executor.ExecuteAsync(command, token);

        if (!cancelBeforeExecution)
        {
            Assert.False(execution.IsCompleted);
            cancellation.Cancel();
            completion.SetCanceled(token);
        }

        var observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await execution);

        Assert.Equal(token, observed.CancellationToken);
        Assert.Equal(token, recordingHandler.Token);
        Assert.Equal(1, recordingHandler.CallCount);
    }

    [Fact]
    public void Reports_a_missing_handler_registration()
    {
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();
        var executor = new CommandExecutor(provider);
        var command = new Command();

        Assert.Throws<InvalidOperationException>(() =>
        {
            executor.ExecuteAsync(command);
        });
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<Command>, RecordingHandler>();
        var options = new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true };
        return services.BuildServiceProvider(options);
    }

    private sealed record Command;

    private sealed class RecordingHandler : ICommandHandler<Command>
    {
        public RecordingHandler()
        {
        }

        public Command? Command { get; private set; }
        public CancellationToken Token { get; private set; }
        public int CallCount { get; private set; }
        public ValueTask Completion { get; set; }
        public Exception? Failure { get; set; }

        public ValueTask HandleAsync(Command command, CancellationToken cancellationToken)
        {
            Command = command;
            Token = cancellationToken;
            CallCount++;

            if (Failure is not null)
            {
                throw Failure;
            }

            return Completion;
        }
    }
}
