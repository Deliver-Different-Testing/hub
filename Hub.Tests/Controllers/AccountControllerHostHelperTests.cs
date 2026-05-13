using Hub.Controllers;

namespace Hub.Tests.Controllers;

public class AccountControllerHostHelperTests
{
    // BuildDestinationHubHost — prod (no env subdomain)

    [Theory]
    [InlineData("hub.urgent.deliverdifferent.com", "courierx", "hub.courierx.deliverdifferent.com")]
    [InlineData("despatch.urgent.deliverdifferent.com", "courierx", "hub.courierx.deliverdifferent.com")]
    public void BuildDestinationHubHost_Prod_ReplacesTenantSegment(string source, string destCode, string expected)
    {
        Assert.Equal(expected, AccountController.BuildDestinationHubHost(source, destCode));
    }

    [Fact]
    public void BuildDestinationHubHost_Prod_DfrntDestination_ReturnsNull()
    {
        // The dfrnt stack tenant only exists in staging. In prod there is no
        // env-bare URL to redirect to.
        Assert.Null(AccountController.BuildDestinationHubHost("hub.urgent.deliverdifferent.com", "dfrnt"));
    }

    // BuildDestinationHubHost — staging

    [Fact]
    public void BuildDestinationHubHost_Staging_RegularTenant_PreservesEnvSegment()
    {
        Assert.Equal(
            "hub.courierx.staging.deliverdifferent.com",
            AccountController.BuildDestinationHubHost("hub.urgent.staging.deliverdifferent.com", "courierx"));
    }

    [Fact]
    public void BuildDestinationHubHost_Staging_RegularToDfrnt_DropsTenantSegment()
    {
        // The original bug: switching to dfrnt from a regular staging tenant
        // produced hub.dfrnt.staging.deliverdifferent.com (DNS does not resolve).
        // dfrnt lives at the bare staging hostname.
        Assert.Equal(
            "hub.staging.deliverdifferent.com",
            AccountController.BuildDestinationHubHost("hub.urgent.staging.deliverdifferent.com", "dfrnt"));
    }

    [Fact]
    public void BuildDestinationHubHost_Staging_DfrntToRegular_PreservesEnvSegment()
    {
        // The symmetric bug: switching from the dfrnt-bare host to a regular
        // tenant rewrote parts[1] = tenant and dropped the staging env entirely,
        // landing the user on prod.
        Assert.Equal(
            "hub.urgent.staging.deliverdifferent.com",
            AccountController.BuildDestinationHubHost("hub.staging.deliverdifferent.com", "urgent"));
    }

    [Fact]
    public void BuildDestinationHubHost_Staging_DfrntSelfSwitch_StaysBare()
    {
        Assert.Equal(
            "hub.staging.deliverdifferent.com",
            AccountController.BuildDestinationHubHost("hub.staging.deliverdifferent.com", "dfrnt"));
    }

    // BuildDestinationHubHost — local/dev (no cross-domain switching)

    [Theory]
    [InlineData("hub.local.deliverdifferent.com", "urgent")]
    [InlineData("hub.local.deliverdifferent.com", "dfrnt")]
    [InlineData("hub.dev.deliverdifferent.com", "urgent")]
    public void BuildDestinationHubHost_LocalOrDev_ReturnsNull(string source, string destCode)
    {
        // Local and dev share a single host across tenants — switching is cookie-only.
        // Returning null causes the frontend to fall back to in-place reload.
        Assert.Null(AccountController.BuildDestinationHubHost(source, destCode));
    }

    // BuildDestinationHubHost — guards

    [Theory]
    [InlineData("", "urgent")]
    [InlineData("hub.urgent.deliverdifferent.com", "")]
    [InlineData("hub.urgent.deliverdifferent.com", "   ")]
    [InlineData("localhost", "urgent")]
    [InlineData("hub.example.com", "urgent")]
    [InlineData("deliverdifferent.com", "urgent")]
    public void BuildDestinationHubHost_BadInputs_ReturnsNull(string source, string destCode)
    {
        Assert.Null(AccountController.BuildDestinationHubHost(source, destCode));
    }

    [Fact]
    public void BuildDestinationHubHost_DestCodeIsCaseInsensitive_ForStackTenant()
    {
        Assert.Equal(
            "hub.staging.deliverdifferent.com",
            AccountController.BuildDestinationHubHost("hub.urgent.staging.deliverdifferent.com", "DFRNT"));
    }

    // ExtractTenantFromHost

    [Theory]
    [InlineData("hub.urgent.deliverdifferent.com", "urgent")]
    [InlineData("despatch.courierx.deliverdifferent.com", "courierx")]
    [InlineData("hub.urgent.staging.deliverdifferent.com", "urgent")]
    public void ExtractTenantFromHost_RegularHosts_ReturnsTenantSegment(string host, string expected)
    {
        Assert.Equal(expected, AccountController.ExtractTenantFromHost(host));
    }

    [Fact]
    public void ExtractTenantFromHost_StagingBareHost_ReturnsDfrnt()
    {
        // Without this, the AcceptTenantSwitchToken endpoint at the staging-bare
        // host would reject every inbound SSO redirect because it could not
        // identify the host's tenant.
        Assert.Equal("dfrnt", AccountController.ExtractTenantFromHost("hub.staging.deliverdifferent.com"));
    }

    [Theory]
    [InlineData("hub.local.deliverdifferent.com")]
    [InlineData("hub.dev.deliverdifferent.com")]
    public void ExtractTenantFromHost_LocalOrDev_ReturnsNull(string host)
    {
        Assert.Null(AccountController.ExtractTenantFromHost(host));
    }

    [Theory]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("hub.example.com")]
    [InlineData("deliverdifferent.com")]
    public void ExtractTenantFromHost_BadInputs_ReturnsNull(string host)
    {
        Assert.Null(AccountController.ExtractTenantFromHost(host));
    }
}
