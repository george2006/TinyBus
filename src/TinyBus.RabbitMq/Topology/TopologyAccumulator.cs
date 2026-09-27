using System.Collections.Generic;
using TinyBus;

namespace TinyBus.RabbitMq;

internal sealed class TopologyAccumulator
{
    private readonly Dictionary<ContractIdentity, ServiceIdentity> commandOwners = new();

    public TopologyDeclarationResult Apply(TopologyDeclaration declaration)
    {
        var conflict = FindConflict(declaration);
        if (conflict is not null)
        {
            var rejected = new TopologyDeclarationResult(false, conflict);
            return rejected;
        }

        RegisterCommandOwners(declaration);

        var accepted = new TopologyDeclarationResult(true, null);
        return accepted;
    }

    public bool TryGetCommandOwner(ContractIdentity contract, out ServiceIdentity owner)
    {
        return commandOwners.TryGetValue(contract, out owner);
    }

    private TopologyConflict? FindConflict(TopologyDeclaration declaration)
    {
        var candidateOwner = new ServiceIdentity(declaration.Service);

        foreach (var contract in declaration.Commands)
        {
            var contractIdentity = contract.ToContractIdentity();
            if (!commandOwners.TryGetValue(contractIdentity, out var existingOwner))
            {
                continue;
            }

            if (existingOwner == candidateOwner)
            {
                continue;
            }

            var conflict = new TopologyConflict(contractIdentity, existingOwner, candidateOwner);
            return conflict;
        }

        return null;
    }

    private void RegisterCommandOwners(TopologyDeclaration declaration)
    {
        var owner = new ServiceIdentity(declaration.Service);

        foreach (var contract in declaration.Commands)
        {
            var contractIdentity = contract.ToContractIdentity();
            commandOwners[contractIdentity] = owner;
        }
    }
}
