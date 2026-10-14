using System.Net;
using System.Text;
using Hub.Features.AgentAccess;

namespace Hub.Tests.Features.AgentAccess;

public class ClientMetadataFetcherTests
{
    private static readonly Uri Url = new("https://claude.ai/oauth/mcp-client");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Seen { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen = request;
            return respond(request, cancellationToken);
        }
    }

    private static HttpResponseMessage Json(string body, string mediaType = "application/json", HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    private static (ClientMetadataFetcher Fetcher, StubHandler Handler) Build(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, ClientMetadataOptions? options = null)
    {
        var handler = new StubHandler(respond);
        return (new ClientMetadataFetcher(new HttpClient(handler), options ?? ClientMetadataOptions.Create(["claude.ai"])), handler);
    }

    [Fact]
    public async Task A_json_document_is_returned_and_the_request_asks_for_json_and_nothing_else()
    {
        var (fetcher, handler) = Build((_, _) => Task.FromResult(Json("""{"a":1}""")));

        var result = await fetcher.FetchAsync(Url, Ct);

        Assert.Equal("""{"a":1}""", result.Body);
        Assert.Null(result.Error);
        Assert.Equal(HttpMethod.Get, handler.Seen!.Method);
        Assert.Equal("application/json", handler.Seen.Headers.Accept.Single().MediaType);
        Assert.Null(handler.Seen.Headers.Authorization);
        Assert.False(handler.Seen.Headers.Contains("Cookie"));
    }

    [Fact]
    public async Task Structured_json_media_types_are_accepted()
    {
        var (fetcher, _) = Build((_, _) => Task.FromResult(Json("{}", "application/client-metadata+json")));

        Assert.Equal("{}", (await fetcher.FetchAsync(Url, Ct)).Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.MovedPermanently)]
    public async Task Anything_but_a_200_is_a_failure_and_redirects_are_not_followed(HttpStatusCode status)
    {
        var (fetcher, _) = Build((_, _) => Task.FromResult(Json("{}", status: status)));

        var result = await fetcher.FetchAsync(Url, Ct);

        Assert.Null(result.Body);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    public async Task Only_json_is_accepted(string mediaType)
    {
        var (fetcher, _) = Build((_, _) => Task.FromResult(Json("{}", mediaType)));

        Assert.NotNull((await fetcher.FetchAsync(Url, Ct)).Error);
    }

    [Fact]
    public async Task A_document_over_the_size_cap_is_refused_whether_or_not_it_says_how_big_it_is()
    {
        var options = ClientMetadataOptions.Create(["claude.ai"]) with { MaxBytes = 100 };
        var big = "{\"pad\":\"" + new string('x', 200) + "\"}";

        var (declared, _) = Build((_, _) => Task.FromResult(Json(big)), options);
        Assert.NotNull((await declared.FetchAsync(Url, Ct)).Error);

        var (streamed, _) = Build((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(big))) { Headers = { ContentType = new("application/json") } }
        }), options);
        Assert.NotNull((await streamed.FetchAsync(Url, Ct)).Error);
    }

    [Fact]
    public async Task A_document_exactly_at_the_cap_is_accepted()
    {
        var options = ClientMetadataOptions.Create(["claude.ai"]) with { MaxBytes = 7 };
        var (fetcher, _) = Build((_, _) => Task.FromResult(Json("""{"a":1}""")), options);

        Assert.Equal("""{"a":1}""", (await fetcher.FetchAsync(Url, Ct)).Body);
    }

    [Fact]
    public async Task A_slow_server_times_out_into_a_failure_not_an_exception()
    {
        var options = ClientMetadataOptions.Create(["claude.ai"]) with { Timeout = TimeSpan.FromMilliseconds(50) };
        var (fetcher, _) = Build(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return Json("{}");
        }, options);

        var result = await fetcher.FetchAsync(Url, Ct);

        Assert.Equal("The request timed out.", result.Error);
    }

    [Fact]
    public async Task Network_errors_are_failures_not_exceptions()
    {
        var (fetcher, _) = Build((_, _) => throw new HttpRequestException("connection refused"));

        var result = await fetcher.FetchAsync(Url, Ct);

        Assert.Contains("refused", result.Error);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_swallowed()
    {
        using var cts = new CancellationTokenSource();
        var (fetcher, _) = Build(async (_, ct) =>
        {
            await cts.CancelAsync();
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return Json("{}");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetcher.FetchAsync(Url, cts.Token));
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("169.254.169.254")]
    [InlineData("192.168.0.1")]
    [InlineData("::1")]
    public async Task Hosts_that_resolve_to_internal_addresses_are_refused_at_connect_time(string host)
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => SsrfSafeConnect.ResolvePublicAddressAsync(host, Ct));
    }

    [Fact]
    public async Task A_public_address_literal_is_allowed_without_any_network_call()
    {
        Assert.Equal(IPAddress.Parse("8.8.8.8"), await SsrfSafeConnect.ResolvePublicAddressAsync("8.8.8.8", Ct));
    }

    [Theory]
    [InlineData("https://claude.ai/oauth/mcp-client", true)]
    [InlineData("https://claude.ai/oauth/", true)]
    [InlineData("https://claude.ai/oauthevil/x", false)]
    [InlineData("https://claude.ai/artifacts/abc", false)]
    [InlineData("https://claude.ai/Oauth/x", false)]
    [InlineData("https://other.test/oauth/x", false)]
    public void An_entry_with_a_path_prefix_only_allows_urls_under_that_path(string url, bool expected)
    {
        var options = ClientMetadataOptions.Create(["claude.ai/oauth/"]);

        Assert.Equal(expected, options.IsAllowed(new Uri(url)));
    }

    [Fact]
    public void Allowed_hosts_are_matched_exactly_and_case_insensitively_and_empty_means_off()
    {
        var options = ClientMetadataOptions.Create([" Claude.AI ", "", "claude.ai"]);

        Assert.Equal(["claude.ai"], options.AllowedHosts);
        Assert.True(options.IsAllowed(new Uri("https://CLAUDE.ai/oauth/x")));
        Assert.False(options.IsAllowed(new Uri("https://evil.claude.ai/oauth/x")));
        Assert.False(options.IsAllowed(new Uri("https://claude.ai.evil.test/oauth/x")));
        Assert.True(options.Enabled);
        Assert.False(ClientMetadataOptions.Create(null).Enabled);
        Assert.False(ClientMetadataOptions.Create([]).Enabled);
    }
}
