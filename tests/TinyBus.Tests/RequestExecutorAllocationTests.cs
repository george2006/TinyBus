using Microsoft.Extensions.DependencyInjection;

namespace TinyBus.Tests;

public sealed class RequestExecutorAllocationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Adds_no_dispatch_allocations_after_warmup(bool completesAsynchronously)
    {
        var services = new ServiceCollection();
        services.AddScoped<IRequestHandler<Request, Response>, Handler>();
        var options = new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true };
        using var provider = services.BuildServiceProvider(options);
        using var scope = provider.CreateScope();
        var executor = new RequestExecutor(scope.ServiceProvider);
        var handler = scope.ServiceProvider.GetRequiredService<IRequestHandler<Request, Response>>();
        var concreteHandler = (Handler)handler;
        var completion = new TaskCompletionSource<Response>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new Request(42);
        var expectedResponse = new Response(42);
        concreteHandler.Completion = ValueTask.FromResult(expectedResponse);

        if (completesAsynchronously)
        {
            // Reuse one task-backed gate to isolate dispatch from handler-owned task allocations.
            concreteHandler.Completion = new ValueTask<Response>(completion.Task);
        }

        MeasureDirectCalls(handler, request);
        MeasureExecutorCalls(executor, request);

        var direct = MeasureDirectCalls(handler, request);
        var dispatched = MeasureExecutorCalls(executor, request);

        Assert.Equal(0L, direct.AllocatedBytes);
        Assert.Equal(0L, dispatched.AllocatedBytes);
        Assert.Equal(!completesAsynchronously, dispatched.Completion.IsCompleted);
        Assert.Equal(42, concreteHandler.LastValue);

        completion.SetResult(expectedResponse);
        var directResponse = await direct.Completion;
        var dispatchedResponse = await dispatched.Completion;
        Assert.Equal(expectedResponse, directResponse);
        Assert.Equal(expectedResponse, dispatchedResponse);
    }

    private static AllocationMeasurement MeasureDirectCalls(
        IRequestHandler<Request, Response> handler,
        Request request)
    {
        var completion = default(ValueTask<Response>);
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 10_000; index++)
        {
            completion = handler.HandleAsync(request, CancellationToken.None);
        }

        var after = GC.GetAllocatedBytesForCurrentThread();
        var measurement = new AllocationMeasurement(after - before, completion);
        return measurement;
    }

    private static AllocationMeasurement MeasureExecutorCalls(
        RequestExecutor executor,
        Request request)
    {
        var completion = default(ValueTask<Response>);
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 10_000; index++)
        {
            completion = executor.ExecuteAsync<Request, Response>(request);
        }

        var after = GC.GetAllocatedBytesForCurrentThread();
        var measurement = new AllocationMeasurement(after - before, completion);
        return measurement;
    }

    private readonly record struct AllocationMeasurement(long AllocatedBytes, ValueTask<Response> Completion);

    private readonly record struct Request(int Value);

    private readonly record struct Response(int Value);

    private sealed class Handler : IRequestHandler<Request, Response>
    {
        public Handler()
        {
        }

        public ValueTask<Response> Completion { get; set; }
        public int LastValue { get; private set; }

        public ValueTask<Response> HandleAsync(Request request, CancellationToken cancellationToken)
        {
            LastValue = request.Value;
            return Completion;
        }
    }
}
