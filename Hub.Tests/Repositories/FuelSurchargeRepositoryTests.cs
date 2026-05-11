using Hub.Models;
using Hub.Repositories;
using Hub.Tests.Helpers;

namespace Hub.Tests.Repositories;

public class FuelSurchargeRepositoryTests
{
    private static readonly DateTime Now = new(2026, 5, 11, 9, 0, 0, DateTimeKind.Unspecified);

    private static (FuelSurchargeRepository repo, DynamicDespatchDbContext ctx) Create()
    {
        var ctx = TestDespatchContextFactory.CreateWithSeedData();
        ctx.TblFuelSurcharges.AddRange(
            new TblFuelSurcharge
            {
                FuelSurchargeId = 1, ClientId = null, Rate = 5.25m, PumpPrice = 2.40m,
                Start = Now.AddDays(-30), End = null, Active = true,
                Created = Now, CreatedBy = "t", LastModified = Now, LastModifiedBy = "t"
            },
            new TblFuelSurcharge
            {
                FuelSurchargeId = 2, ClientId = null, Rate = 4.10m, PumpPrice = 2.20m,
                Start = Now.AddDays(-60), End = Now.AddDays(-30), Active = false,
                Created = Now, CreatedBy = "t", LastModified = Now, LastModifiedBy = "t"
            },
            new TblFuelSurcharge
            {
                FuelSurchargeId = 3, ClientId = 1, Rate = 6.00m, PumpPrice = 2.50m,
                Start = Now.AddDays(-10), End = null, Active = true,
                Created = Now, CreatedBy = "t", LastModified = Now, LastModifiedBy = "t"
            },
            new TblFuelSurcharge
            {
                FuelSurchargeId = 4, ClientId = 2, Rate = 7.00m, PumpPrice = null,
                Start = Now.AddDays(-5), End = null, Active = true,
                Created = Now, CreatedBy = "t", LastModified = Now, LastModifiedBy = "t"
            },
            new TblFuelSurcharge
            {
                FuelSurchargeId = 5, ClientId = null, Rate = 9.99m, PumpPrice = 3.00m,
                Start = Now.AddDays(10), End = null, Active = true, // future start
                Created = Now, CreatedBy = "t", LastModified = Now, LastModifiedBy = "t"
            });
        ctx.SaveChanges();
        return (new FuelSurchargeRepository(ctx), ctx);
    }

    [Fact]
    public async Task GetHistoryAsync_NullClient_ReturnsOnlyStandardRows()
    {
        var (repo, _) = Create();

        var rows = await repo.GetHistoryAsync(clientId: null, Now, TestContext.Current.CancellationToken);

        Assert.All(rows, r => Assert.Null(r.ClientId));
        Assert.Equal(3, rows.Count); // ids 1, 2, 5
    }

    [Fact]
    public async Task GetHistoryAsync_ClientId_ReturnsStandardAndClientRows()
    {
        var (repo, _) = Create();

        var rows = await repo.GetHistoryAsync(clientId: 1, Now, TestContext.Current.CancellationToken);

        Assert.Equal(4, rows.Count); // 3 standard + 1 client-1 row
        Assert.Contains(rows, r => r.FuelSurchargeId == 3 && r.ClientId == 1 && r.ClientName == "Test Client");
        Assert.DoesNotContain(rows, r => r.ClientId == 2);
    }

    [Fact]
    public async Task GetHistoryAsync_OrdersByStartDescending()
    {
        var (repo, _) = Create();

        var rows = await repo.GetHistoryAsync(clientId: 1, Now, TestContext.Current.CancellationToken);

        var starts = rows.Select(r => r.Start).ToList();
        Assert.Equal(starts.OrderByDescending(s => s).ToList(), starts);
    }

    [Fact]
    public async Task GetHistoryAsync_IsCurrent_TrueWhenActiveAndStarted()
    {
        var (repo, _) = Create();

        var rows = await repo.GetHistoryAsync(clientId: null, Now, TestContext.Current.CancellationToken);

        Assert.True(rows.Single(r => r.FuelSurchargeId == 1).IsCurrent);
        Assert.False(rows.Single(r => r.FuelSurchargeId == 2).IsCurrent); // inactive
        Assert.False(rows.Single(r => r.FuelSurchargeId == 5).IsCurrent); // future start
    }

    [Fact]
    public async Task GetHistoryAsync_Limit_CapsResults()
    {
        var (repo, _) = Create();

        var rows = await repo.GetHistoryAsync(clientId: 1, Now, TestContext.Current.CancellationToken, limit: 2);

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task GetHistoryAsync_OnException_ReturnsEmptyList()
    {
        var (repo, ctx) = Create();
        await ctx.DisposeAsync();

        var rows = await repo.GetHistoryAsync(clientId: null, Now, TestContext.Current.CancellationToken);

        Assert.Empty(rows);
    }
}
