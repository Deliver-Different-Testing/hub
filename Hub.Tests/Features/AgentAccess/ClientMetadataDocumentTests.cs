using System.Net;
using Hub.Features.AgentAccess;

namespace Hub.Tests.Features.AgentAccess;

// Phase 1.8: a client ID metadata document is untrusted input from the internet. Only a small, strict subset is accepted.
public class ClientMetadataDocumentTests
{
    private const string Url = "https://claude.ai/oauth/mcp-client";

    private static string Doc(string? clientId = Url, string extra = "", string redirects = """["https://claude.ai/api/mcp/auth_callback"]""", string name = "Claude") =>
        $$"""{"client_id":"{{clientId}}","client_name":"{{name}}","redirect_uris":{{redirects}}{{extra}}}""";

    [Fact]
    public void A_valid_document_is_accepted()
    {
        var result = ClientMetadataDocument.Parse(Doc(), Url);

        Assert.Null(result.Error);
        Assert.Equal("Claude", result.Document!.ClientName);
        Assert.Equal(["https://claude.ai/api/mcp/auth_callback"], result.Document.RedirectUris);
    }

    [Fact]
    public void The_client_id_must_equal_the_url_it_was_fetched_from_exactly()
    {
        Assert.NotNull(ClientMetadataDocument.Parse(Doc(clientId: "https://claude.ai/oauth/other"), Url).Error);
        Assert.NotNull(ClientMetadataDocument.Parse(Doc(clientId: Url + "/"), Url).Error);
        Assert.NotNull(ClientMetadataDocument.Parse(Doc(clientId: Url.ToUpperInvariant()), Url).Error);
        Assert.NotNull(ClientMetadataDocument.Parse("""{"client_name":"x","redirect_uris":["https://a.test/cb"]}""", Url).Error);
    }

    [Theory]
    [InlineData("""[]""")]
    [InlineData("""["http://claude.ai/cb"]""")]
    [InlineData("""["https://claude.ai/cb#frag"]""")]
    [InlineData("""["/relative"]""")]
    [InlineData("""["javascript:alert(1)"]""")]
    [InlineData("""["https://claude.ai/cb", "http://evil.test/cb"]""")]
    [InlineData("""[1]""")]
    [InlineData("""{"a":1}""")]
    [InlineData("""null""")]
    public void Redirect_uris_must_be_a_non_empty_list_of_https_uris_without_fragments(string redirects)
    {
        Assert.NotNull(ClientMetadataDocument.Parse(Doc(redirects: redirects), Url).Error);
    }

    [Fact]
    public void Too_many_or_too_long_redirect_uris_are_refused()
    {
        var many = "[" + string.Join(",", Enumerable.Range(0, 11).Select(i => $"\"https://claude.ai/cb{i}\"")) + "]";
        var longUri = $"[\"https://claude.ai/{new string('a', 400)}\"]";

        Assert.NotNull(ClientMetadataDocument.Parse(Doc(redirects: many), Url).Error);
        Assert.NotNull(ClientMetadataDocument.Parse(Doc(redirects: longUri), Url).Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_name_is_required(string name)
    {
        Assert.NotNull(ClientMetadataDocument.Parse(Doc(name: name), Url).Error);
    }

    [Fact]
    public void The_name_is_trimmed_cleaned_of_control_characters_and_capped()
    {
        var cleaned = ClientMetadataDocument.Parse(Doc(name: "  Cla\\u0000ude\\n  "), Url);
        var longName = ClientMetadataDocument.Parse(Doc(name: new string('n', 300)), Url);

        Assert.Null(cleaned.Error);
        Assert.DoesNotContain(cleaned.Document!.ClientName, char.IsControl);
        Assert.Equal(cleaned.Document.ClientName, cleaned.Document.ClientName.Trim());
        Assert.Equal(100, longName.Document!.ClientName.Length);
    }

    [Theory]
    [InlineData(""","token_endpoint_auth_method":"client_secret_basic" """)]
    [InlineData(""","token_endpoint_auth_method":"private_key_jwt" """)]
    [InlineData(""","client_secret":"x" """)]
    [InlineData(""","client_secret_expires_at":0 """)]
    [InlineData(""","jwks_uri":"https://claude.ai/jwks" """)]
    [InlineData(""","jwks":{"keys":[]} """)]
    [InlineData(""","grant_types":["client_credentials"] """)]
    [InlineData(""","grant_types":["authorization_code","password"] """)]
    [InlineData(""","response_types":["token"] """)]
    public void Confidential_or_broader_clients_are_refused(string extra)
    {
        Assert.NotNull(ClientMetadataDocument.Parse(Doc(extra: extra), Url).Error);
    }

    [Theory]
    [InlineData(""","token_endpoint_auth_method":"none" """)]
    [InlineData(""","grant_types":["authorization_code","refresh_token"] """)]
    [InlineData(""","grant_types":["authorization_code"] """)]
    [InlineData(""","response_types":["code"] """)]
    [InlineData(""","logo_uri":"https://claude.ai/logo.png","unknown":1 """)]
    public void Public_code_flow_clients_and_unknown_fields_are_fine(string extra)
    {
        Assert.Null(ClientMetadataDocument.Parse(Doc(extra: extra), Url).Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("{")]
    public void Anything_that_is_not_a_json_object_is_refused(string body)
    {
        Assert.NotNull(ClientMetadataDocument.Parse(body, Url).Error);
    }

    [Theory]
    [InlineData("https://claude.ai/oauth/mcp-client", true)]
    [InlineData("https://claude.ai/a", true)]
    [InlineData("http://claude.ai/oauth/mcp-client", false)]
    [InlineData("https://claude.ai", false)]
    [InlineData("https://claude.ai/", false)]
    [InlineData("https://claude.ai/x#frag", false)]
    [InlineData("https://claude.ai:8443/x", false)]
    [InlineData("https://user:pass@claude.ai/x", false)]
    [InlineData("https://127.0.0.1/x", false)]
    [InlineData("https://[::1]/x", false)]
    [InlineData("https://localhost/x", false)]
    [InlineData("https://claude.ai/x?y=1", true)]
    [InlineData("https://claude.ai/../x", false)]
    [InlineData("claude.ai/x", false)]
    [InlineData("dfrnt-claude", false)]
    [InlineData("", false)]
    public void Only_plain_https_urls_with_a_path_and_a_real_host_name_are_client_ids(string value, bool expected)
    {
        Assert.Equal(expected, ClientMetadataDocument.IsAcceptableClientIdUrl(value, out _));
    }

    [Fact]
    public void A_client_id_url_longer_than_the_store_allows_is_not_acceptable()
    {
        Assert.False(ClientMetadataDocument.IsAcceptableClientIdUrl("https://claude.ai/" + new string('a', 90), out _));
        Assert.True(ClientMetadataDocument.IsAcceptableClientIdUrl("https://claude.ai/" + new string('a', 70), out _));
    }

    [Theory]
    [InlineData("10.0.0.1", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("192.0.2.1", false)]
    [InlineData("::1", false)]
    [InlineData("::", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fd12:3456::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("::ffff:169.254.169.254", false)]
    [InlineData("ff02::1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("160.79.104.10", true)]
    [InlineData("172.32.0.1", true)]
    [InlineData("2606:4700:4700::1111", true)]
    public void Only_public_addresses_may_be_fetched(string address, bool expected)
    {
        Assert.Equal(expected, SsrfGuard.IsPublic(IPAddress.Parse(address)));
    }
}
