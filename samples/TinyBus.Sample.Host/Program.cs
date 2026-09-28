using Microsoft.Extensions.DependencyInjection;
using TinyBus;
using TinyBus.Sample.Contracts;
using TinyBus.Sample.Host;

var serviceName = args.Length > 0 ? args[0] : "commerce";
var services = new ServiceCollection();
services.AddTinyBus(bus =>
{
    bus.Service(serviceName);
});

var options = new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true };
using var provider = services.BuildServiceProvider(options);
var topology = provider.GetRequiredService<ServiceTopology>();
WriteTopology(topology);

using var scope = provider.CreateScope();

await ExecuteCommands(scope.ServiceProvider);
await ExecuteRequest(scope.ServiceProvider);

static void WriteTopology(ServiceTopology topology)
{
    Console.WriteLine($"Service: {topology.Service.Value}");
    foreach (var message in topology.Messages)
    {
        var description = string.Join(" | ",
            message.Contract.Name,
            message.Contract.Version,
            message.Kind,
            message.MessageType.FullName,
            message.HandlerType.FullName,
            message.ResponseType?.FullName ?? "-");
        Console.WriteLine(description);
    }
}

static async ValueTask ExecuteCommands(IServiceProvider services)
{
    var executor = new CommandExecutor(services);
    var rebuild = new RebuildReadModel();
    await executor.ExecuteAsync(rebuild);

    var paymentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    var capture = new CapturePayment(paymentId, 100m);
    await executor.ExecuteAsync(capture);
}

static async ValueTask ExecuteRequest(IServiceProvider services)
{
    // Direct local invocation demonstrates the handler contract; transport request/reply comes later.
    var handler = services.GetRequiredService<IRequestHandler<GetPaymentStatus, PaymentStatus>>();
    var paymentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    var request = new GetPaymentStatus(paymentId);
    var response = await handler.HandleAsync(request, CancellationToken.None);

    Console.WriteLine($"Payment status: {response.PaymentId} | {response.Status}");
}
