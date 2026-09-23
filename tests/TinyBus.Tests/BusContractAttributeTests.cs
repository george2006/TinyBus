namespace TinyBus.Tests;

public sealed class BusContractAttributeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Rejects_invalid_contract_names(string? name)
    {
        Assert.ThrowsAny<ArgumentException>(() => new BusContractAttribute(name!));
    }

    [Fact]
    public void Preserves_explicit_contract_identity()
    {
        var contract = new BusContractAttribute("orders.order-placed")
        {
            Version = 2
        };

        Assert.Equal("orders.order-placed", contract.Name);
        Assert.Equal(2, contract.Version);
    }

    [Fact]
    public void Defaults_contract_version_to_one()
    {
        var contract = new BusContractAttribute("orders.order-placed");

        Assert.Equal(1, contract.Version);
    }
}
