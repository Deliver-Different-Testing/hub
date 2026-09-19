using Hub.ViewModels;

namespace Hub.Tests.ViewModels;

public class FuelSurchargeRowTests
{
    [Fact]
    public void ScopeLabel_NullClientId_ReturnsStandard()
    {
        var row = new FuelSurchargeRow { ClientId = null };

        Assert.Equal("Standard", row.ScopeLabel);
    }

    [Fact]
    public void ScopeLabel_HasClientId_ReturnsClientSpecific()
    {
        var row = new FuelSurchargeRow { ClientId = 42 };

        Assert.Equal("Client-specific", row.ScopeLabel);
    }
}
