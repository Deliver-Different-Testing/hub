using Hub.Features.AiAssistant;

namespace Hub.Tests.Features.AiAssistant;

public class CspKnowledgeOptionsTests
{
    [Theory]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("https://dfrntcrm.example.com", false)]
    [InlineData("http://dfrntcrm.example.com", true)]
    public void Validate_EmptyAndHttps_AreValidInProduction_HttpIsNot(string? url, bool expectError)
    {
        var error = CspKnowledgeOptions.Validate(url, isDevelopment: false);

        Assert.Equal(expectError, error is not null);
    }

    [Theory]
    [InlineData("http://localhost:5100")]
    [InlineData("https://dfrntcrm.example.com")]
    public void Validate_DevelopmentAllowsHttpAndHttps(string url) =>
        Assert.Null(CspKnowledgeOptions.Validate(url, isDevelopment: true));

    [Theory]
    [InlineData("not a url", false)]
    [InlineData("not a url", true)]
    [InlineData("ftp://csp.example.com", true)]
    public void Validate_RejectsRelativeAndOtherSchemes(string url, bool isDevelopment) =>
        Assert.NotNull(CspKnowledgeOptions.Validate(url, isDevelopment));

    [Fact]
    public void Resolve_ConfiguredUrlWins()
    {
        var url = CspKnowledgeOptions.Resolve("https://csp.example.com", "https://app_name.example.com");

        Assert.Equal("https://csp.example.com", url);
    }

    [Fact]
    public void Resolve_DerivesFromTenantUrl_WhenNothingIsConfigured()
    {
        Assert.Equal("https://dfrntcrm.example.com", CspKnowledgeOptions.Resolve(null, "https://app_name.example.com"));
        Assert.Equal("https://dfrntcrm.example.com", CspKnowledgeOptions.Resolve("  ", "https://app_name.example.com"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("https://example.com/no-placeholder")]
    public void Resolve_IsOff_WhenThereIsNothingToDeriveFrom(string? tenantUrl) =>
        Assert.Null(CspKnowledgeOptions.Resolve(null, tenantUrl));
}
