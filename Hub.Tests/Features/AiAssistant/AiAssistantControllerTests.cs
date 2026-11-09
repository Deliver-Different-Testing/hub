using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Hub.Controllers;
using Hub.Controllers.Api;
using Hub.Features.AiAssistant;
using Hub.Tests.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using NSubstitute;

namespace Hub.Tests.Features.AiAssistant;

public class AiAssistantControllerTests
{
    private readonly IAiAssistantService _service = Substitute.For<IAiAssistantService>();
    private readonly AiAssistantOptions _options = new() { Federation = TestFederation.Connected() };

    private AiAssistantController Create(out DefaultHttpContext http)
    {
        var controller = new AiAssistantController(_service, _options);
        ControllerTestBase.SetupHttpContext(controller);
        http = (DefaultHttpContext)controller.HttpContext;
        http.Response.Body = new MemoryStream();
        return controller;
    }

    private static async IAsyncEnumerable<AiAssistantStreamEvent> Events(params AiAssistantStreamEvent[] events)
    {
        foreach (var evt in events)
        {
            yield return evt;
        }

        await Task.CompletedTask;
    }

    private static string BodyOf(HttpContext http)
    {
        http.Response.Body.Position = 0;
        return new StreamReader(http.Response.Body, Encoding.UTF8).ReadToEnd();
    }

    private static AiChatRequest Req() => new() { Messages = [new AiChatMessage { Role = "user", Content = "hi" }] };

    [Fact]
    public void Controller_RequiresAuthentication_UnderTheAssistantRoute_WithAntiforgeryAndRateLimiting()
    {
        var type = typeof(AiAssistantController);

        Assert.NotNull(type.GetCustomAttributes(typeof(AuthorizeAttribute), true).SingleOrDefault());
        Assert.Empty(type.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        Assert.Equal("api/assistant", ((RouteAttribute)type.GetCustomAttributes(typeof(RouteAttribute), true).Single()).Template);
        Assert.True(typeof(SpaApiControllerBase).IsAssignableFrom(type));
        Assert.Equal(
            AssistantRateLimiting.PolicyName,
            ((EnableRateLimitingAttribute)type.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).Single()).PolicyName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Status_ReflectsWhetherClaudeIsConnected(bool enabled)
    {
        _options.Federation = enabled ? TestFederation.Connected() : new AiAssistantOptions.FederationOptions();

        var ok = Assert.IsType<OkObjectResult>(Create(out _).Status());

        Assert.Equal(JsonSerializer.Serialize(new { enabled }), JsonSerializer.Serialize(ok.Value));
    }

    [Fact]
    public async Task Messages_StreamsFramesAsCamelCaseServerSentEvents()
    {
        _service.ChatStreamAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>()).Returns(Events(
            new AiAssistantStreamEvent { Type = "tool_use", ToolName = "get_article" },
            new AiAssistantStreamEvent { Type = "text_delta", Text = "Hi" },
            new AiAssistantStreamEvent
            {
                Type = "done",
                Sources = [new KbSource(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Title", false)]
            }));
        var controller = Create(out var http);

        await controller.Messages(Req(), CancellationToken.None);

        Assert.Equal("text/event-stream", http.Response.ContentType);
        Assert.Equal(
            "data: {\"type\":\"tool_use\",\"toolName\":\"get_article\"}\n\n" +
            "data: {\"type\":\"text_delta\",\"text\":\"Hi\"}\n\n" +
            "data: {\"type\":\"done\",\"sources\":[{\"articleId\":\"11111111-1111-1111-1111-111111111111\",\"title\":\"Title\",\"isFaq\":false}]}\n\n",
            BodyOf(http));
    }

    [Fact]
    public async Task Messages_DoneWithoutSources_OmitsTheField()
    {
        _service.ChatStreamAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(Events(new AiAssistantStreamEvent { Type = "done" }));
        var controller = Create(out var http);

        await controller.Messages(Req(), CancellationToken.None);

        Assert.Equal("data: {\"type\":\"done\"}\n\n", BodyOf(http));
    }

    [Fact]
    public async Task Messages_ErrorBeforeAnyOutput_IsARealHttpStatusWithJson()
    {
        _service.ChatStreamAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(Events(new AiAssistantStreamEvent { Type = "error", Error = "bad", HttpStatus = 400 }));
        var controller = Create(out var http);

        await controller.Messages(Req(), CancellationToken.None);

        Assert.Equal(400, http.Response.StatusCode);
        Assert.Contains("\"error\":\"bad\"", BodyOf(http));
    }

    [Fact]
    public async Task Messages_WhenNotConnected_Is503_WithoutCallingTheService()
    {
        _options.Federation = new AiAssistantOptions.FederationOptions();
        var controller = Create(out var http);

        await controller.Messages(Req(), CancellationToken.None);

        Assert.Equal(503, http.Response.StatusCode);
        Assert.Contains("Sorry, I'm not connected right now. Please try again later.", BodyOf(http));
        _service.DidNotReceiveWithAnyArgs().ChatStreamAsync(null!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Messages_ClientDisconnect_IsSwallowed()
    {
        using var cts = new CancellationTokenSource();
        _service.ChatStreamAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>()).Returns(_ => Cancelling(cts));
        var controller = Create(out _);

        await controller.Messages(Req(), cts.Token);
    }

    private static async IAsyncEnumerable<AiAssistantStreamEvent> Cancelling(CancellationTokenSource cts)
    {
        yield return new AiAssistantStreamEvent { Type = "text_delta", Text = "x" };
        await cts.CancelAsync();
        cts.Token.ThrowIfCancellationRequested();
        yield break;
    }

    // ---- rate limiting ------------------------------------------------------------------------

    [Fact]
    public void PartitionKey_PrefersTheUserIdClaim()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("UserID", "42"), new Claim(ClaimTypes.Name, "alice@example.com")], "test"));

        Assert.Equal("user:42", AssistantRateLimiting.PartitionKey(principal));
    }

    [Fact]
    public void PartitionKey_FallsBackToTheNameClaim_ThenAnonymous()
    {
        var named = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "bob")], "test"));

        Assert.Equal("user:bob", AssistantRateLimiting.PartitionKey(named));
        Assert.Equal("anonymous", AssistantRateLimiting.PartitionKey(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public async Task RateLimit_Is20PerMinutePerUser_Then429()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            AssistantRateLimiting.Add(o);
        });
        await using var app = builder.Build();
        app.Use((ctx, next) =>
        {
            var user = ctx.Request.Headers["X-Test-User"].ToString();
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("UserID", user)], "test"));
            return next();
        });
        app.UseRateLimiter();
        app.MapGet("/ping", () => "pong").RequireRateLimiting(AssistantRateLimiting.PolicyName);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        async Task<int> Hit(string user)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/ping");
            request.Headers.Add("X-Test-User", user);
            return (int)(await client.SendAsync(request)).StatusCode;
        }

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(200, await Hit("1"));
        }

        Assert.Equal(429, await Hit("1"));
        Assert.Equal(200, await Hit("2"));
    }
}
