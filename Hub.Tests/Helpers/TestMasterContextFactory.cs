using Hub.Models.Master;
using Microsoft.EntityFrameworkCore;

namespace Hub.Tests.Helpers;

public static class TestMasterContextFactory
{
    private static MasterContext Create(string? dbName = null)
    {
        dbName ??= Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<MasterContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var context = new MasterContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public static MasterContext CreateWithSeedData(string? dbName = null)
    {
        var context = Create(dbName);
        SeedData(context);
        return context;
    }

    private static void SeedData(MasterContext context)
    {
        var tenant = new Tenant
        {
            TenantId = 1,
            Name = "Test Tenant",
            Dbconnection = "Server=test;Database=TestDB;",
            Code = "test",
            CountryCode = "NZ",
            TimeZone = "New Zealand Standard Time"
        };

        var tenant2 = new Tenant
        {
            TenantId = 2,
            Name = "Second Tenant",
            Dbconnection = "Server=test;Database=SecondDB;",
            Code = "second",
            CountryCode = "AU",
            TimeZone = "AUS Eastern Standard Time"
        };

        context.Tenants.AddRange(tenant, tenant2);

        var staffUser = new User
        {
            UserId = 1,
            Email = "staff@test.com",
            Password = "HASHEDPASSWORD",
            Salt = "12345",
            CurrentTenantId = 1,
            IsLegacyHash = false,
            IsCourier = false
        };

        var courierUser = new User
        {
            UserId = 2,
            Email = "courier@test.com",
            Password = "COURIERPASSWORD",
            Salt = "54321",
            CurrentTenantId = 1,
            IsLegacyHash = false,
            IsCourier = true
        };

        var legacyUser = new User
        {
            UserId = 3,
            Email = "legacy@test.com",
            Password = "LEGACYPASSWORD",
            Salt = "11111",
            CurrentTenantId = 1,
            IsLegacyHash = true,
            IsCourier = false
        };

        var resetUser = new User
        {
            UserId = 4,
            Email = "reset@test.com",
            Password = "RESETPASSWORD",
            Salt = "22222",
            ResetKey = "valid-reset-key",
            CurrentTenantId = 1,
            IsLegacyHash = false,
            IsCourier = false
        };

        context.Users.AddRange(staffUser, courierUser, legacyUser, resetUser);

        context.TenantUsers.AddRange(
            new TenantUser { TenantUserId = 1, TenantId = 1, UserId = 1 },
            new TenantUser { TenantUserId = 2, TenantId = 2, UserId = 1 },
            new TenantUser { TenantUserId = 3, TenantId = 1, UserId = 2 },
            new TenantUser { TenantUserId = 4, TenantId = 1, UserId = 3 },
            new TenantUser { TenantUserId = 5, TenantId = 1, UserId = 4 }
        );

        context.TenantUserSettings.Add(new TenantUserSetting
        {
            TenantUserSettingId = 1,
            TenantId = 1,
            UserId = 1,
            SettingName = "Theme",
            SettingValue = "Dark"
        });

        context.TenantBrandings.Add(new TenantBranding
        {
            TenantId = 1,
            CompanyName = "Test Company",
            AddressLine1 = "123 Test St",
            AddressLine2 = "Suite 100",
            AddressLine3 = null,
            City = "Auckland",
            Region = "Auckland",
            PostalCode = "1010",
            Country = "New Zealand",
            Phone = "+64 9 123 4567",
            Email = "info@test.com",
            Website = "https://test.com",
            PrimaryColour = "#FF0000",
            HeaderTextColour = "#FFFFFF",
            AccentColour = "#00FF00",
            FooterText = "Test Footer",
            DisclaimerText = "Test Disclaimer",
            PaperSize = "A4",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        context.SaveChanges();
    }
}
