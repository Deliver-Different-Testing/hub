using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hub.Tests.Security;

public class CookieSecurityTests
{
    [Fact]
    public void AuthCookie_InProduction_RequiresSecure()
    {
        var options = GetAuthCookieOptions("Production");

        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
    }

    [Fact]
    public void AuthCookie_InDevelopment_AllowsNonSecure()
    {
        var options = GetAuthCookieOptions("Development");

        Assert.Equal(CookieSecurePolicy.SameAsRequest, options.Cookie.SecurePolicy);
    }

    [Fact]
    public void AuthCookie_IsHttpOnly()
    {
        var options = GetAuthCookieOptions("Production");

        Assert.True(options.Cookie.HttpOnly);
    }

    [Fact]
    public void AuthCookie_HasSameSiteLax()
    {
        var options = GetAuthCookieOptions("Production");

        Assert.Equal(SameSiteMode.Lax, options.Cookie.SameSite);
    }

    [Fact]
    public void SessionCookie_InProduction_RequiresSecure()
    {
        var options = GetSessionOptions("Production");

        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
    }

    [Fact]
    public void SessionCookie_InDevelopment_AllowsNonSecure()
    {
        var options = GetSessionOptions("Development");

        Assert.Equal(CookieSecurePolicy.SameAsRequest, options.Cookie.SecurePolicy);
    }

    [Fact]
    public void SessionCookie_IsHttpOnly()
    {
        var options = GetSessionOptions("Production");

        Assert.True(options.Cookie.HttpOnly);
    }

    [Fact]
    public void SessionCookie_HasSameSiteStrict()
    {
        var options = GetSessionOptions("Production");

        Assert.Equal(SameSiteMode.Strict, options.Cookie.SameSite);
    }

    [Fact]
    public void SessionCookie_TimeoutIsReasonable()
    {
        var options = GetSessionOptions("Production");

        Assert.True(options.IdleTimeout <= TimeSpan.FromMinutes(30),
            $"Session timeout of {options.IdleTimeout.TotalMinutes} minutes exceeds 30 minute maximum");
    }

    private static CookieAuthenticationOptions GetAuthCookieOptions(string environment)
    {
        var cookieSecurePolicy = environment == "Development"
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication("Identity.Application")
            .AddCookie("Identity.Application", options =>
            {
                options.Cookie.Name = ".AspNet.SharedCookie";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = cookieSecurePolicy;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.LoginPath = "/Account/Login";
            });

        var sp = services.BuildServiceProvider();
        var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        return optionsMonitor.Get("Identity.Application");
    }

    private static SessionOptions GetSessionOptions(string environment)
    {
        var cookieSecurePolicy = environment == "Development"
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSession(options =>
        {
            options.Cookie.Name = "hub_session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = cookieSecurePolicy;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.IdleTimeout = TimeSpan.FromMinutes(30);
        });

        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptions<SessionOptions>>().Value;
    }
}
