using Hub.Controllers.Api;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Hub.Tests.Controllers.Api;

public class AntiforgeryApiControllerTests
{
    private static string IssueCookie(string environment)
    {
        var antiforgery = Substitute.For<IAntiforgery>();
        antiforgery.GetAndStoreTokens(Arg.Any<HttpContext>())
            .Returns(new AntiforgeryTokenSet("request-token", "cookie-token", "form", "X-XSRF-TOKEN"));
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(environment);

        var controller = new AntiforgeryApiController(antiforgery, env)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        Assert.IsType<NoContentResult>(controller.Get());
        return controller.Response.Headers.SetCookie.ToString();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Deployed_environments_get_a_secure_cookie(string environment)
    {
        // Kestrel sees plain http behind the load balancer, so this can't follow the request scheme.
        var cookie = IssueCookie(environment);

        Assert.StartsWith("XSRF-TOKEN=request-token", cookie);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("httponly", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Development_drops_secure_so_the_cookie_survives_the_http_vite_dev_server()
    {
        var cookie = IssueCookie(Environments.Development);

        Assert.StartsWith("XSRF-TOKEN=request-token", cookie);
        Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }
}
