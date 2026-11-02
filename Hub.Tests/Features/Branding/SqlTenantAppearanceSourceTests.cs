using Hub.Features.Branding;
using Hub.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Hub.Tests.Features.Branding;

public class SqlTenantAppearanceSourceTests
{
    [Fact]
    public async Task ReadsTheSettingsRowColumn()
    {
        await using var context = TestDespatchContextFactory.CreateWithSeedData();
        var setting = await context.TblSettings.AsTracking().SingleAsync(TestContext.Current.CancellationToken);
        setting.AppearanceDefaultsJson = """{"primary":"#13b964"}""";
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var source = new SqlTenantAppearanceSource(new ConnectionStringManager(), context);

        var json = await source.ReadJsonAsync("Server=x;", TestContext.Current.CancellationToken);

        Assert.Equal("""{"primary":"#13b964"}""", json);
    }

    [Fact]
    public async Task ReturnsNullWhenNothingIsStored()
    {
        await using var context = TestDespatchContextFactory.CreateWithSeedData();
        var source = new SqlTenantAppearanceSource(new ConnectionStringManager(), context);

        Assert.Null(await source.ReadJsonAsync("Server=x;", TestContext.Current.CancellationToken));
    }
}
