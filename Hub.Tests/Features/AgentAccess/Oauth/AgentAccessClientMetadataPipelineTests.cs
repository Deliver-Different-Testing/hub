using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Hub.Features.AgentAccess.ClientMetadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
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

        var (page, _) = await Oauth.AgentAccessAuthorizePipelineTests.Page(app, Oauth.AgentAccessAuthorizePipelineTests.AuthorizeUrl(clientId: CimdUrl), Oauth.AgentAccessAuthorizePipelineTests.Staff());

        Assert.Equal("Claude Connector", page.GetProperty("clientName").GetString());
        // The name is the document's own claim; the host it was fetched from is what DFRNT vouches for.
        Assert.Equal("claude.ai", page.GetProperty("clientHost").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, page.GetProperty("verifiedVendor").ValueKind);
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
        await Fetcher(app).DidNotReceiveWithAnyArgs().FetchAsync(null!, TestContext.Current.CancellationToken);
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
        await Fetcher(app).DidNotReceiveWithAnyArgs().FetchAsync(null!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task With_the_feature_off_a_url_client_id_is_just_unknown()
    {
        await using var app = await Oauth.AgentAccessAuthorizePipelineTests.StartAsync(metadataHosts: []);

        var response = await Oauth.AgentAccessAuthorizePipelineTests.Get(app, Oauth.AgentAccessAuthorizePipelineTests.AuthorizeUrl(clientId: CimdUrl), Oauth.AgentAccessAuthorizePipelineTests.Staff());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await Fetcher(app).DidNotReceiveWithAnyArgs().FetchAsync(null!, TestContext.Current.CancellationToken);
    }

    // ---- private_key_jwt clients (ChatGPT) ----------------------------------

    private const string CimdJwksUrl = "https://claude.ai/oauth/jwks.json";

    private static string SigningDocument() =>
        JsonSerializer.Serialize(new
        {
            client_id = CimdUrl,
            client_name = "ChatGPT",
            redirect_uris = new[] { Oauth.AgentAccessAuthorizePipelineTests.RedirectUri },
            token_endpoint_auth_method = "private_key_jwt",
            token_endpoint_auth_methods_supported = new[] { "none", "private_key_jwt" },
            grant_types = new[] { "authorization_code", "refresh_token", "urn:ietf:params:oauth:grant-type:jwt-bearer" },
            jwks_uri = CimdJwksUrl
        });

    private static string PublicKeySet(RSA rsa, string kid)
    {
        var key = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(rsa.ExportParameters(includePrivateParameters: false)));
        key.Kid = kid;
        key.Use = JsonWebKeyUseNames.Sig;
        key.Alg = SecurityAlgorithms.RsaSha256;
        return JsonSerializer.Serialize(new { keys = new[] { new { kty = key.Kty, kid = key.Kid, use = key.Use, alg = key.Alg, n = key.N, e = key.E } } });
    }

    // RFC 7523 section 3: iss = sub = client_id, aud = this authorization server.
    private static string ClientAssertion(RSA rsa, string kid, string audience, string type = "client-authentication+jwt") =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            TokenType = type,
            Issuer = CimdUrl,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", CimdUrl), new Claim("jti", Guid.NewGuid().ToString("N"))]),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = kid }, SecurityAlgorithms.RsaSha256)
        });

    private static Task<HttpResponseMessage> TokenWithAssertion(WebApplication app, string code, string? assertion) =>
        Oauth.AgentAccessAuthorizePipelineTests.Client(app).PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = CimdUrl,
            ["code"] = code,
            ["redirect_uri"] = Oauth.AgentAccessAuthorizePipelineTests.RedirectUri,
            ["code_verifier"] = Oauth.AgentAccessAuthorizePipelineTests.Verifier,
            ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            ["client_assertion"] = assertion ?? string.Empty
        }.Where(p => p.Value.Length > 0)), Oauth.AgentAccessAuthorizePipelineTests.Ct);

    private static async Task<string> CodeFor(WebApplication app)
    {
        var response = await Oauth.AgentAccessAuthorizePipelineTests.Consent(app, Oauth.AgentAccessAuthorizePipelineTests.AuthorizeUrl(clientId: CimdUrl), "allow");
        return Oauth.AgentAccessAuthorizePipelineTests.AssertRedirectToClient(response)["code"]!;
    }

    // OpenIddict 7's own shape (draft-ietf-oauth-rfc7523bis), and the RFC 7523 shape ChatGPT may still send.
    [Theory]
    [InlineData("client-authentication+jwt", "")]
    [InlineData("JWT", "")]
    [InlineData("JWT", "/connect/token")]
    [InlineData("client-authentication+jwt", "/connect/token")]
    public async Task A_private_key_jwt_client_redeems_its_code_with_an_assertion_signed_by_its_published_key(string type, string audiencePath)
    {
        await using var app = await Oauth.AgentAccessAuthorizePipelineTests.StartAsync();
        using var rsa = RSA.Create(2048);
        ServeDocument(app, SigningDocument());
        Fetcher(app).FetchAsync(new Uri(CimdJwksUrl), Arg.Any<CancellationToken>()).Returns(new ClientMetadataFetchResult(PublicKeySet(rsa, "k1"), null));

        var code = await CodeFor(app);
        var response = await TokenWithAssertion(app, code, ClientAssertion(rsa, "k1", Oauth.AgentAccessAuthorizePipelineTests.Issuer + audiencePath, type));

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Oauth.AgentAccessAuthorizePipelineTests.Ct));
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Oauth.AgentAccessAuthorizePipelineTests.Ct)).RootElement;
        Assert.True(body.TryGetProperty("refresh_token", out _));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("other key")]
    [InlineData("other audience")]
    public async Task A_private_key_jwt_client_cannot_redeem_its_code_without_a_valid_assertion(string fault)
    {
        await using var app = await Oauth.AgentAccessAuthorizePipelineTests.StartAsync();
        using var rsa = RSA.Create(2048);
        using var other = RSA.Create(2048);
        ServeDocument(app, SigningDocument());
        Fetcher(app).FetchAsync(new Uri(CimdJwksUrl), Arg.Any<CancellationToken>()).Returns(new ClientMetadataFetchResult(PublicKeySet(rsa, "k1"), null));

        var assertion = fault switch
        {
            "missing" => null,
            "other key" => ClientAssertion(other, "k1", Oauth.AgentAccessAuthorizePipelineTests.Issuer),
            _ => ClientAssertion(rsa, "k1", "https://elsewhere.example.test/connect/token")
        };
        var response = await TokenWithAssertion(app, await CodeFor(app), assertion);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("invalid_", await response.Content.ReadAsStringAsync(Oauth.AgentAccessAuthorizePipelineTests.Ct));
    }

    // RFC 9207: ChatGPT only uses its stable client document and redirect when every authorization response names the
    // issuer, errors included.
    [Fact]
    public async Task Authorization_responses_name_the_issuer_whether_the_user_allows_or_denies()
    {
        await using var app = await Oauth.AgentAccessAuthorizePipelineTests.StartAsync();
        ServeDocument(app, CimdDocument());

        // Deny first: once allowed, a later request skips the consent page.
        var denied = Oauth.AgentAccessAuthorizePipelineTests.AssertRedirectToClient(
            await Oauth.AgentAccessAuthorizePipelineTests.Consent(app, Oauth.AgentAccessAuthorizePipelineTests.AuthorizeUrl(clientId: CimdUrl), "deny"));
        var allowed = Oauth.AgentAccessAuthorizePipelineTests.AssertRedirectToClient(
            await Oauth.AgentAccessAuthorizePipelineTests.Consent(app, Oauth.AgentAccessAuthorizePipelineTests.AuthorizeUrl(clientId: CimdUrl), "allow"));
        var discovery = JsonDocument.Parse(await Oauth.AgentAccessAuthorizePipelineTests.Client(app)
            .GetStringAsync("/.well-known/oauth-authorization-server", Oauth.AgentAccessAuthorizePipelineTests.Ct)).RootElement;

        var issuer = discovery.GetProperty("issuer").GetString();
        Assert.StartsWith(Oauth.AgentAccessAuthorizePipelineTests.Issuer, issuer);
        Assert.True(discovery.GetProperty("authorization_response_iss_parameter_supported").GetBoolean());
        Assert.Equal(issuer, allowed["iss"]);
        Assert.Equal("access_denied", denied["error"]);
        Assert.Equal(issuer, denied["iss"]);
    }

    [Fact]
    public async Task Pre_registered_clients_are_unaffected()
    {
        await using var app = await Oauth.AgentAccessAuthorizePipelineTests.StartAsync();

        var response = await Oauth.AgentAccessAuthorizePipelineTests.Get(app, Oauth.AgentAccessAuthorizePipelineTests.AuthorizeUrl(), Oauth.AgentAccessAuthorizePipelineTests.Staff());

        Oauth.AgentAccessAuthorizePipelineTests.AssertSentToConnectPage(response);
        await Fetcher(app).DidNotReceiveWithAnyArgs().FetchAsync(null!, TestContext.Current.CancellationToken);
    }
}
