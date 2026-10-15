using System.Net;
using System.Text.Json;
using Hub.Features.AgentAccess.ClientMetadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Hub.Tests.Features.AgentAccess.Oauth;

// Phase 1.8 end to end: a URL used as client_id is fetched, validated, cached as an application row, and then goes
// through the same authorization, consent and token steps as a pre-registered client.
public partial class AgentAccessAuthorizePipelineTests
{
    private const string CimdUrl = "https://claude.ai/oauth/mcp-client";

    private static string CimdDocument(string callback = Oauth.AgentAccessAuthorizePipelineTests.RedirectUri, string name = "Claude") =>
        JsonSerializer.Serialize(new { client_id = CimdUrl, client_name = name, redirect_uris = new[] { callback } });

    private static IClientMetadataFetcher Fetcher(WebApplication app) => app.Services.GetRequiredService<IClientMetadataFetcher>();

    private static void ServeDocument(WebApplication app, string body) =>
        Fetcher(app).FetchAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns(new ClientMetadataFetchResult(body, null));

    [Fact]
    public async Task Discovery_advertises_client_id_metadata_documents_only_when_hosts_are_allowed()
    {
        await using var on = await Oauth.AgentAccessAuthorizePipelineTests.StartAsync();
        await using var off = await Oauth.AgentAccessAuthorizePipelineTests.StartAsync(metadataHosts: []);

        var enabled = JsonDocument.Parse(await Oauth.AgentAccessAuthorizePipelineTests.Client(on).GetStringAsync("/.well-known/openid-configuration", Oauth.AgentAccessAuthorizePipelineTests.Ct)).RootElement;
        var disabled = JsonDocument.Parse(await Oauth.AgentAccessAuthorizePipelineTests.Client(off).GetStringAsync("/.well-known/openid-configuration", Oauth.AgentAccessAuthorizePipelineTests.Ct)).RootElement;

        Assert.True(enabled.GetProperty("client_id_metadata_document_supported").GetBoolean());
        Assert.False(disabled.TryGetProperty("client_id_metadata_document_supported", out var value) && value.GetBoolean());
    }

    [Fact]
    public async Task A_metadata_document_client_goes_through_consent_and_token_exchange_like_any_other()
    {
        await using var app = await Oauth.AgentAccessAuthorizePipelineTests.StartAsync();
        ServeDocument(app, CimdDocument());

        var response = await Oauth.AgentAccessAuthorizePipelineTests.Consent(app, Oauth.AgentAccessAuthorizePipelineTests.AuthorizeUrl(clientId: CimdUrl), "allow");
        var code = Oauth.AgentAccessAuthorizePipelineTests.AssertRedirectToClient(response)["code"]!;
        var tokenResponse = await Oauth.AgentAccessAuthorizePipelineTests.Token(app, code, Oauth.AgentAccessAuthorizePipelineTests.Verifier, clientId: CimdUrl);

        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        await Fetcher(app).Received(1).FetchAsync(new Uri(CimdUrl), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_consent_hand_off_names_the_app_from_its_document()
    {
        await using var app = await Oauth.AgentAccessAuthorizePipelineTests.StartAsync();
        ServeDocument(app, CimdDocument(name: "Claude Connector"));

        var page = await Oauth.AgentAccessAuthorizePipelineTests.Get(app, Oauth.AgentAccessAuthorizePipelineTests.AuthorizeUrl(clientId: CimdUrl), Oauth.AgentAccessAuthorizePipelineTests.Staff());
        var html = await page.Content.ReadAsStringAsync(Oauth.AgentAccessAuthorizePipelineTests.Ct);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Claude Connector", html);
        Assert.Contains("Claude Connector (claude.ai)", System.Net.WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task A_redirect_uri_the_document_does_not_list_is_refused_and_no_code_is_issued()
    {
        await using var app = await Oauth.AgentAccessAuthorizePipelineTests.StartAsync();
        ServeDocument(app, CimdDocument(callback: "https://claude.ai/api/mcp/other_callback"));

        var response = await Oauth.AgentAccessAuthorizePipelineTests.Get(app, Oauth.AgentAccessAuthorizePipelineTests.AuthorizeUrl(clientId: CimdUrl), Oauth.AgentAccessAuthorizePipelineTests.Staff());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("code=", response.Headers.Location?.OriginalString ?? string.Empty);
    }

    [Theory]
    [InlineData("https://evil.example.test/oauth/mcp-client")]
    [InlineData("https://claude.ai.evil.test/oauth/mcp-client")]
    public async Task A_host_that_is_not_allowed_is_never_fetched(string clientId)
    {
        await using var app = await Oauth.AgentAccessAuthorizePipelineTests.StartAsync();

        var response = await Oauth.AgentAccessAuthorizePipelineTests.Get(app, Oauth.AgentAccessAuthorizePipelineTests.AuthorizeUrl(clientId: clientId), Oauth.AgentAccessAuthorizePipelineTests.Staff());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("invalid_client", await response.Content.ReadAsStringAsync(Oauth.AgentAccessAuthorizePipelineTests.Ct));
        await Fetcher(app).DidNotReceiveWithAnyArgs().FetchAsync(default!, default);
    }

    [Fact]
    public async Task A_document_that_cannot_be_fetched_or_is_invalid_gives_invalid_client()
    {
        await using var app = await Oauth.AgentAccessAuthorizePipelineTests.StartAsync();
        Fetcher(app).FetchAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns(new ClientMetadataFetchResult(null, "HTTP 500"));

        var failed = await Oauth.AgentAccessAuthorizePipelineTests.Get(app, Oauth.AgentAccessAuthorizePipelineTests.AuthorizeUrl(clientId: CimdUrl), Oauth.AgentAccessAuthorizePipelineTests.Staff());
        Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        Assert.Contains("invalid_client", await failed.Content.ReadAsStringAsync(Oauth.AgentAccessAuthorizePipelineTests.Ct));
    }

    [Fact]
    public async Task A_confidential_looking_document_is_refused()
    {
        await using var app = await Oauth.AgentAccessAuthorizePipelineTests.StartAsync();
        ServeDocument(app, """{"client_id":"https://claude.ai/oauth/mcp-client","client_name":"x","redirect_uris":["https://claude.ai/api/mcp/auth_callback"],"client_secret":"s"}""");

        var response = await Oauth.AgentAccessAuthorizePipelineTests.Get(app, Oauth.AgentAccessAuthorizePipelineTests.AuthorizeUrl(clientId: CimdUrl), Oauth.AgentAccessAuthorizePipelineTests.Staff());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_token_endpoint_never_triggers_a_fetch_for_a_client_it_has_not_seen()
    {
        await using var app = await Oauth.AgentAccessAuthorizePipelineTests.StartAsync();

        var response = await Oauth.AgentAccessAuthorizePipelineTests.Token(app, "anything", Oauth.AgentAccessAuthorizePipelineTests.Verifier, clientId: CimdUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await Fetcher(app).DidNotReceiveWithAnyArgs().FetchAsync(default!, default);
    }

    [Fact]
    public async Task With_the_feature_off_a_url_client_id_is_just_unknown()
    {
        await using var app = await Oauth.AgentAccessAuthorizePipelineTests.StartAsync(metadataHosts: []);

        var response = await Oauth.AgentAccessAuthorizePipelineTests.Get(app, Oauth.AgentAccessAuthorizePipelineTests.AuthorizeUrl(clientId: CimdUrl), Oauth.AgentAccessAuthorizePipelineTests.Staff());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await Fetcher(app).DidNotReceiveWithAnyArgs().FetchAsync(default!, default);
    }

    [Fact]
    public async Task Pre_registered_clients_are_unaffected()
    {
        await using var app = await Oauth.AgentAccessAuthorizePipelineTests.StartAsync();

        var response = await Oauth.AgentAccessAuthorizePipelineTests.Get(app, Oauth.AgentAccessAuthorizePipelineTests.AuthorizeUrl(), Oauth.AgentAccessAuthorizePipelineTests.Staff());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await Fetcher(app).DidNotReceiveWithAnyArgs().FetchAsync(default!, default);
    }
}
