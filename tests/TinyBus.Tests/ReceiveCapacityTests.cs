namespace TinyBus.Tests;

public sealed class ReceiveCapacityTests
{
    [Fact]
    public void Preserves_maximum_and_available_capacity()
    {
        var capacity = new ReceiveCapacity(maximum: 16, available: 7);

        Assert.Equal(16, capacity.Maximum);
        Assert.Equal(7, capacity.Available);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(8, 0)]
    [InlineData(8, 9)]
    public void Rejects_invalid_capacity(int maximum, int available)
    {
        void CreateCapacity()
        {
            _ = new ReceiveCapacity(maximum, available);
        }

        Assert.Throws<ArgumentOutOfRangeException>(CreateCapacity);
    }
}
