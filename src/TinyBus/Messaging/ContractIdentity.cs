using System;
using System.Reflection;

namespace TinyBus;

public readonly record struct ContractIdentity(
    string Name,
    int Version)
{
    internal static ContractIdentity From(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        var attribute = messageType.GetCustomAttribute<BusContractAttribute>();
        if (attribute is not null)
        {
            var explicitContract = FromAttribute(attribute);

            return explicitContract;
        }

        EnsureConventionIsUnambiguous(messageType);
        var fullName = messageType.FullName;
        if (fullName is null)
        {
            throw new InvalidOperationException("A bus message must have a fully-qualified CLR name.");
        }

        var contractName = fullName.Replace('+', '.');
        var contract = new ContractIdentity(contractName, 1);

        return contract;
    }

    private static ContractIdentity FromAttribute(BusContractAttribute attribute)
    {
        if (attribute.Version <= 0)
        {
            throw new InvalidOperationException("A bus contract version must be greater than zero.");
        }

        var contract = new ContractIdentity(attribute.Name, attribute.Version);

        return contract;
    }

    private static void EnsureConventionIsUnambiguous(Type messageType)
    {
        if (!messageType.IsArray && !messageType.IsGenericType)
        {
            return;
        }

        var error = $"Message type '{messageType}' requires BusContractAttribute because its "
            + "conventional wire name cannot be reproduced safely at runtime.";
        throw new InvalidOperationException(error);
    }
}
