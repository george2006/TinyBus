using Microsoft.Extensions.DependencyInjection;

namespace TinyBus.Tests;

public sealed class RequestExecutorTests
{
    [Fact]
    public async Task Forwards_the_request_and_token_to_the_scoped_handler()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        using var otherScope = provider.CreateScope();
        using var cancellation = new CancellationTokenSource();
        var executor = new RequestExecutor(scope.ServiceProvider);
        var handler = scope.ServiceProvider.GetRequiredService<IRequestHandler<Request, Response>>();
        var recordingHandler = (RecordingHandler)handler;
        var otherHandler = otherScope.ServiceProvider.GetRequiredService<IRequestHandler<Request, Response>>();
        var otherRecordingHandler = (RecordingHandler)otherHandler;
        var request = new Request();
        var token = cancellation.Token;

        var expectedResponse = new Response(42);
        recordingHandler.Completion = ValueTask.FromResult(expectedResponse);

        var response = await executor.ExecuteAsync<Request, Response>(request, token);
        var repeatedResponse = await executor.ExecuteAsync<Request, Response>(request, token);

        Assert.Same(expectedResponse, response);
        Assert.Same(expectedResponse, repeatedResponse);

        Assert.Same(request, recordingHandler.Request);
        Assert.Equal(token, recordingHandler.Token);
        Assert.Equal(2, recordingHandler.CallCount);
        Assert.Equal(0, otherRecordingHandler.CallCount);
    }

    [Fact]
    public async Task Preserves_pending_completion()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var executor = new RequestExecutor(scope.ServiceProvider);
        var handler = scope.ServiceProvider.GetRequiredService<IRequestHandler<Request, Response>>();
        var recordingHandler = (RecordingHandler)handler;
        var completion = new TaskCompletionSource<Response>(TaskCreationOptions.RunContinuationsAsynchronously);
        recordingHandler.Completion = new ValueTask<Response>(completion.Task);
        var request = new Request();

        var execution = executor.ExecuteAsync<Request, Response>(request);

        Assert.False(execution.IsCompleted);
        var expectedResponse = new Response(42);
        completion.SetResult(expectedResponse);
        var response = await execution;

        Assert.Same(expectedResponse, response);
    }

    [Fact]
    public void Preserves_a_synchronous_handler_failure()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var executor = new RequestExecutor(scope.ServiceProvider);
        var handler = scope.ServiceProvider.GetRequiredService<IRequestHandler<Request, Response>>();
        var recordingHandler = (RecordingHandler)handler;
        var failure = new InvalidOperationException("Handler failed.");
        recordingHandler.Failure = failure;
        var request = new Request();

        var observed = Assert.Throws<InvalidOperationException>(() =>
        {
            executor.ExecuteAsync<Request, Response>(request);
        });

        Assert.Same(failure, observed);
    }

    [Fact]
    public async Task Preserves_an_asynchronous_handler_failure()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var executor = new RequestExecutor(scope.ServiceProvider);
        var handler = scope.ServiceProvider.GetRequiredService<IRequestHandler<Request, Response>>();
        var recordingHandler = (RecordingHandler)handler;
        var completion = new TaskCompletionSource<Response>(TaskCreationOptions.RunContinuationsAsynchronously);
        recordingHandler.Completion = new ValueTask<Response>(completion.Task);
        var failure = new InvalidOperationException("Handler failed asynchronously.");
        var request = new Request();

        var execution = executor.ExecuteAsync<Request, Response>(request);
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
        var executor = new RequestExecutor(scope.ServiceProvider);
        var handler = scope.ServiceProvider.GetRequiredService<IRequestHandler<Request, Response>>();
        var recordingHandler = (RecordingHandler)handler;
        var completion = new TaskCompletionSource<Response>(TaskCreationOptions.RunContinuationsAsynchronously);
        recordingHandler.Completion = new ValueTask<Response>(completion.Task);
        var request = new Request();
        var token = cancellation.Token;

        if (cancelBeforeExecution)
        {
            cancellation.Cancel();
            completion.SetCanceled(token);
        }

        var execution = executor.ExecuteAsync<Request, Response>(request, token);

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
        var executor = new RequestExecutor(provider);
        var request = new Request();

        Assert.Throws<InvalidOperationException>(() =>
        {
            executor.ExecuteAsync<Request, Response>(request);
        });
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<IRequestHandler<Request, Response>, RecordingHandler>();
        var options = new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true };
        return services.BuildServiceProvider(options);
    }

    private sealed record Request;

    private sealed record Response(int Value);

    private sealed class RecordingHandler : IRequestHandler<Request, Response>
    {
        public RecordingHandler()
        {
            var response = new Response(42);
            Completion = ValueTask.FromResult(response);
        }

        public Request? Request { get; private set; }
        public CancellationToken Token { get; private set; }
        public int CallCount { get; private set; }
        public ValueTask<Response> Completion { get; set; }
        public Exception? Failure { get; set; }

        public ValueTask<Response> HandleAsync(Request request, CancellationToken cancellationToken)
        {
            Request = request;
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
