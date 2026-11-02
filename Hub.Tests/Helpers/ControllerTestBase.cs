using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace Hub.Tests.Helpers;

public static class ControllerTestBase
{
    public static void SetupHttpContext(Controller controller, ClaimsPrincipal? user = null)
    {
        var httpContext = CreateHttpContext(user);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        controller.TempData = new TempDataDictionary(httpContext, Substitute.For<ITempDataProvider>());
    }

    public static void SetupHttpContext(ControllerBase controller, ClaimsPrincipal? user = null) =>
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext(user) };

    public static DefaultHttpContext CreateHttpContext(ClaimsPrincipal? user = null)
    {
        user ??= ClaimsPrincipalFactory.Create();

        var mockAuthService = Substitute.For<IAuthenticationService>();
        mockAuthService.SignInAsync(
                Arg.Any<HttpContext>(),
                Arg.Any<string>(),
                Arg.Any<ClaimsPrincipal>(),
                Arg.Any<AuthenticationProperties>())
            .Returns(Task.CompletedTask);
        mockAuthService.SignOutAsync(
                Arg.Any<HttpContext>(),
                Arg.Any<string>(),
                Arg.Any<AuthenticationProperties>())
            .Returns(Task.CompletedTask);

        var mockUrlHelper = Substitute.For<IUrlHelper>();
        mockUrlHelper.Action(Arg.Any<UrlActionContext>())
            .Returns("/mocked-url");

        var mockUrlHelperFactory = Substitute.For<IUrlHelperFactory>();
        mockUrlHelperFactory.GetUrlHelper(Arg.Any<ActionContext>())
            .Returns(mockUrlHelper);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAuthenticationService))
            .Returns(mockAuthService);
        serviceProvider.GetService(typeof(IUrlHelperFactory))
            .Returns(mockUrlHelperFactory);

        var mockSession = Substitute.For<ISession>();

        var httpContext = new DefaultHttpContext
        {
            User = user,
            RequestServices = serviceProvider
        };
        httpContext.Features.Set<ISessionFeature>(new SessionFeature { Session = mockSession });

        return httpContext;
    }

    private class SessionFeature : ISessionFeature
    {
        public ISession Session { get; set; } = null!;
    }
}
