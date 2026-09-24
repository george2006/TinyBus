using Microsoft.Extensions.DependencyInjection;

namespace TinyBus.Tests;

public sealed class EventExecutorTests
{
    [Fact]
    public async Task Invokes_all_handlers_in_order_with_the_same_message_and_token()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        using var otherScope = provider.CreateScope();
        using var cancellation = new CancellationTokenSource();
        var executor = new EventExecutor(scope.ServiceProvider);
        var log = scope.ServiceProvider.GetRequiredService<InvocationLog>();
        var otherLog = otherScope.ServiceProvider.GetRequiredService<InvocationLog>();
        var message = new Changed();
        var token = cancellation.Token;

        var results = await executor.ExecuteAsync(message, token);

        var expectedOrder = new[] { "first", "second" };
        Assert.Equal(expectedOrder, log.Handlers);
        Assert.Empty(otherLog.Handlers);
        Assert.All(log.Messages, observed => Assert.Same(message, observed));
        Assert.All(log.Tokens, observed => Assert.Equal(token, observed));
        Assert.Equal(2, results.Count);
        var firstResult = new EventHandlerResult(typeof(FirstHandler), true);
        var secondResult = new EventHandlerResult(typeof(SecondHandler), true);
        var expectedResults = new[] { firstResult, secondResult };
        Assert.Equal(expectedResults, results);
    }

    [Fact]
    public async Task Waits_for_each_handler_before_continuing()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var executor = new EventExecutor(scope.ServiceProvider);
        var log = scope.ServiceProvider.GetRequiredService<InvocationLog>();
        var firstCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        log.FirstCompletion = new ValueTask(firstCompletion.Task);
        log.SecondCompletion = new ValueTask(secondCompletion.Task);
        var message = new Changed();

        var execution = executor.ExecuteAsync(message);

        Assert.False(execution.IsCompleted);
        var firstOnly = new[] { "first" };
        Assert.Equal(firstOnly, log.Handlers);

        firstCompletion.SetResult();
        await log.SecondStarted.Task;

        Assert.False(execution.IsCompleted);
        var allHandlers = new[] { "first", "second" };
        Assert.Equal(allHandlers, log.Handlers);

        secondCompletion.SetResult();
        await execution;
    }

    [Fact]
    public async Task Records_a_synchronous_failure_and_attempts_the_next_handler_once()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var executor = new EventExecutor(scope.ServiceProvider);
        var log = scope.ServiceProvider.GetRequiredService<InvocationLog>();
        var failure = new InvalidOperationException("Handler failed.");
        log.FirstFailure = failure;
        var message = new Changed();

        var results = await executor.ExecuteAsync(message);

        var firstResult = new EventHandlerResult(typeof(FirstHandler), false);
        var secondResult = new EventHandlerResult(typeof(SecondHandler), true);
        var expectedResults = new[] { firstResult, secondResult };
        Assert.Equal(expectedResults, results);
        var allHandlers = new[] { "first", "second" };
        Assert.Equal(allHandlers, log.Handlers);
    }

    [Fact]
    public async Task Records_an_asynchronous_failure_and_attempts_the_next_handler_once()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var executor = new EventExecutor(scope.ServiceProvider);
        var log = scope.ServiceProvider.GetRequiredService<InvocationLog>();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        log.FirstCompletion = new ValueTask(completion.Task);
        var failure = new InvalidOperationException("Handler failed asynchronously.");
        var message = new Changed();

        var execution = executor.ExecuteAsync(message);
        Assert.False(execution.IsCompleted);
        completion.SetException(failure);

        var results = await execution;

        var firstResult = new EventHandlerResult(typeof(FirstHandler), false);
        var secondResult = new EventHandlerResult(typeof(SecondHandler), true);
        var expectedResults = new[] { firstResult, secondResult };
        Assert.Equal(expectedResults, results);
        var allHandlers = new[] { "first", "second" };
        Assert.Equal(allHandlers, log.Handlers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Propagates_caller_cancellation(bool cancelBeforeExecution)
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        using var cancellation = new CancellationTokenSource();
        var executor = new EventExecutor(scope.ServiceProvider);
        var log = scope.ServiceProvider.GetRequiredService<InvocationLog>();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        log.FirstCompletion = new ValueTask(completion.Task);
        var message = new Changed();
        var token = cancellation.Token;

        if (cancelBeforeExecution)
        {
            cancellation.Cancel();
            completion.SetCanceled(token);
        }

        var execution = executor.ExecuteAsync(message, token);

        if (!cancelBeforeExecution)
        {
            Assert.False(execution.IsCompleted);
            cancellation.Cancel();
            completion.SetCanceled(token);
        }

        var observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await execution);

        Assert.Equal(token, observed.CancellationToken);
        var firstOnly = new[] { "first" };
        Assert.Equal(firstOnly, log.Handlers);
    }

    [Fact]
    public async Task Records_a_handler_cancellation_as_failure_when_the_caller_is_not_cancelled()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var executor = new EventExecutor(scope.ServiceProvider);
        var log = scope.ServiceProvider.GetRequiredService<InvocationLog>();
        log.FirstFailure = new OperationCanceledException();
        var message = new Changed();

        var results = await executor.ExecuteAsync(message);

        var firstResult = new EventHandlerResult(typeof(FirstHandler), false);
        var secondResult = new EventHandlerResult(typeof(SecondHandler), true);
        var expectedResults = new[] { firstResult, secondResult };
        Assert.Equal(expectedResults, results);
        var allHandlers = new[] { "first", "second" };
        Assert.Equal(allHandlers, log.Handlers);
    }

    [Fact]
    public async Task Completes_successfully_without_handlers()
    {
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();
        var executor = new EventExecutor(provider);
        var message = new Changed();

        var execution = executor.ExecuteAsync(message);

        Assert.True(execution.IsCompletedSuccessfully);
        var results = await execution;
        Assert.Empty(results);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<InvocationLog>();
        services.AddScoped<IEventHandler<Changed>, FirstHandler>();
        services.AddScoped<IEventHandler<Changed>, SecondHandler>();
        var options = new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true };
        return services.BuildServiceProvider(options);
    }

    private sealed record Changed;

    private sealed class InvocationLog
    {
        public InvocationLog()
        {
        }

        public List<string> Handlers { get; } = new();
        public List<Changed> Messages { get; } = new();
        public List<CancellationToken> Tokens { get; } = new();
        public ValueTask FirstCompletion { get; set; }
        public ValueTask SecondCompletion { get; set; }
        public Exception? FirstFailure { get; set; }
        public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Record(string handler, Changed message, CancellationToken token)
        {
            Handlers.Add(handler);
            Messages.Add(message);
            Tokens.Add(token);
        }
    }

    private sealed class FirstHandler : IEventHandler<Changed>
    {
        private readonly InvocationLog log;

        public FirstHandler(InvocationLog log)
        {
            this.log = log;
        }

        public ValueTask HandleAsync(Changed message, CancellationToken cancellationToken)
        {
            log.Record("first", message, cancellationToken);

            if (log.FirstFailure is not null)
            {
                throw log.FirstFailure;
            }

            return log.FirstCompletion;
        }
    }

    private sealed class SecondHandler : IEventHandler<Changed>
    {
        private readonly InvocationLog log;

        public SecondHandler(InvocationLog log)
        {
            this.log = log;
        }

        public ValueTask HandleAsync(Changed message, CancellationToken cancellationToken)
        {
            log.Record("second", message, cancellationToken);
            log.SecondStarted.TrySetResult();
            return log.SecondCompletion;
        }
    }
}
