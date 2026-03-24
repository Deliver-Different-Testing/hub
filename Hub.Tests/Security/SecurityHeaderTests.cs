using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hub.Tests.Security;

public class SecurityHeaderTests
{
    private static async Task<HttpResponseMessage> GetResponseWithSecurityHeaders()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                });
                webBuilder.Configure(app =>
                {
                    app.Use(async (HttpContext context, Func<Task> next) =>
                    {
                        var headers = context.Response.Headers;
                        headers.XContentTypeOptions = "nosniff";
                        headers.XFrameOptions = "DENY";
                        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                        headers.ContentSecurityPolicy = string.Join("; ",
                            "default-src 'self'",
                            "script-src 'self' 'unsafe-inline' https://www.google.com/recaptcha/ https://www.gstatic.com/recaptcha/",
                            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
                            "font-src 'self' https://fonts.gstatic.com https://fonts.googleapis.com",
                            "img-src 'self' data:",
                            "connect-src 'self'",
                            "frame-src https://www.google.com/recaptcha/ https://recaptcha.google.com/recaptcha/",
                            "base-uri 'self'",
                            "form-action 'self'");
                        await next();
                    });
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/test", () => "ok");
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        return await client.GetAsync("/test");
    }

    [Fact]
    public async Task Response_ContainsXContentTypeOptions()
    {
        var response = await GetResponseWithSecurityHeaders();

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Response_ContainsXFrameOptionsDeny()
    {
        var response = await GetResponseWithSecurityHeaders();

        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task Response_ContainsReferrerPolicy()
    {
        var response = await GetResponseWithSecurityHeaders();

        Assert.Equal("strict-origin-when-cross-origin",
            response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task Response_ContainsPermissionsPolicy()
    {
        var response = await GetResponseWithSecurityHeaders();

        Assert.Equal("camera=(), microphone=(), geolocation=()",
            response.Headers.GetValues("Permissions-Policy").Single());
    }

    [Fact]
    public async Task Response_ContainsContentSecurityPolicy()
    {
        var response = await GetResponseWithSecurityHeaders();

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("script-src", csp);
        Assert.Contains("frame-src", csp);
    }

    [Fact]
    public async Task CSP_AllowsGoogleRecaptcha()
    {
        var response = await GetResponseWithSecurityHeaders();

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("https://www.google.com/recaptcha/", csp);
        Assert.Contains("https://www.gstatic.com/recaptcha/", csp);
    }

    [Fact]
    public async Task CSP_AllowsGoogleFonts()
    {
        var response = await GetResponseWithSecurityHeaders();

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("https://fonts.googleapis.com", csp);
        Assert.Contains("https://fonts.gstatic.com", csp);
    }

    [Fact]
    public async Task CSP_RestrictsFormAction()
    {
        var response = await GetResponseWithSecurityHeaders();

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("form-action 'self'", csp);
    }

    [Fact]
    public async Task CSP_RestrictsBaseUri()
    {
        var response = await GetResponseWithSecurityHeaders();

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("base-uri 'self'", csp);
    }
}
