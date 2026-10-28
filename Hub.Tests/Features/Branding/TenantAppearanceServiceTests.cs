using Hub.Features.Branding;
using Hub.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace Hub.Tests.Features.Branding;

public class TenantAppearanceServiceTests
{
    private readonly IAuthenticationRepository _master = Substitute.For<IAuthenticationRepository>();
    private readonly ITenantAppearanceSource _source = Substitute.For<ITenantAppearanceSource>();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    private TenantAppearanceService CreateService() => new(_master, _source, _cache);

    [Fact]
    public async Task UnknownTenant_ReportsNotFound()
    {
        _master.GetTenantConnectionStringAsync(9).Returns((string?)null);

        var result = await CreateService().GetAsync(9);

        Assert.Equal(TenantAppearanceStatus.TenantNotFound, result.Status);
        await _source.DidNotReceiveWithAnyArgs().ReadJsonAsync(default!, default);
    }

    [Fact]
    public async Task StoredJson_IsParsed()
    {
        _master.GetTenantConnectionStringAsync(1).Returns("Server=x;");
        _source.ReadJsonAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("""{"primary":"#13B964"}""");

        var result = await CreateService().GetAsync(1);

        Assert.Equal(TenantAppearanceStatus.Found, result.Status);
        Assert.Equal("#13b964", result.Appearance!.Primary);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{bad")]
    [InlineData("""{"primary":"nope"}""")]
    public async Task NullOrUnusableJson_ReportsNone(string? json)
    {
        _master.GetTenantConnectionStringAsync(1).Returns("Server=x;");
        _source.ReadJsonAsync(default!, default).ReturnsForAnyArgs(json);

        var result = await CreateService().GetAsync(1);

        Assert.Equal(TenantAppearanceStatus.None, result.Status);
        Assert.Null(result.Appearance);
    }

    [Fact]
    public async Task Result_IsCachedPerTenant()
    {
        _master.GetTenantConnectionStringAsync(1).Returns("Server=x;");
        _source.ReadJsonAsync(default!, default).ReturnsForAnyArgs("""{"contrast":"high"}""");
        var service = CreateService();

        await service.GetAsync(1);
        await service.GetAsync(1);

        await _source.ReceivedWithAnyArgs(1).ReadJsonAsync(default!, default);
        await _master.Received(1).GetTenantConnectionStringAsync(1);
    }
}
