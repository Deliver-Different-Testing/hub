using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;

namespace Hub.Tests.Helpers;

public static class ControllerTestBase
{
    public static void SetupHttpContext(Controller controller, ClaimsPrincipal? user = null)
    {
        user ??= ClaimsPrincipalFactory.Create();

        var mockAuthService = new Mock<IAuthenticationService>();
        mockAuthService
            .Setup(x => x.SignInAsync(
                It.IsAny<HttpContext>(),
                It.IsAny<string>(),
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<AuthenticationProperties>()))
            .Returns(Task.CompletedTask);
        mockAuthService
            .Setup(x => x.SignOutAsync(
                It.IsAny<HttpContext>(),
                It.IsAny<string>(),
                It.IsAny<AuthenticationProperties>()))
            .Returns(Task.CompletedTask);

        var mockUrlHelper = new Mock<IUrlHelper>();
        mockUrlHelper
            .Setup(x => x.Action(It.IsAny<UrlActionContext>()))
            .Returns("/mocked-url");

        var mockUrlHelperFactory = new Mock<IUrlHelperFactory>();
        mockUrlHelperFactory
            .Setup(f => f.GetUrlHelper(It.IsAny<ActionContext>()))
            .Returns(mockUrlHelper.Object);

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(sp => sp.GetService(typeof(IAuthenticationService)))
            .Returns(mockAuthService.Object);
        serviceProvider
            .Setup(sp => sp.GetService(typeof(IUrlHelperFactory)))
            .Returns(mockUrlHelperFactory.Object);

        var mockSession = new Mock<ISession>();

        var httpContext = new DefaultHttpContext
        {
            User = user,
            RequestServices = serviceProvider.Object
        };
        httpContext.Features.Set<ISessionFeature>(new SessionFeature { Session = mockSession.Object });

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
    }

    private class SessionFeature : ISessionFeature
    {
        public ISession Session { get; set; } = null!;
    }
}
