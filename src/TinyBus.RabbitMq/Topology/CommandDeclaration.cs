using System.Text.Json.Serialization;
using TinyBus;

namespace TinyBus.RabbitMq;

internal sealed record CommandDeclaration(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] int Version)
{
    public static CommandDeclaration Create(ContractIdentity contract)
    {
        var declaration = new CommandDeclaration(contract.Name, contract.Version);
        return declaration;
    }

    public ContractIdentity ToContractIdentity()
    {
        var contract = new ContractIdentity(Name, Version);
        return contract;
    }
}
