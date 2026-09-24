using TinyBus;

var service = new ServiceIdentity(args.Length > 0 ? args[0] : "commerce");
var manifest = new TinyBus.Generated.GeneratedTinyBusManifest();
var topology = manifest.CreateTopology(service);

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
