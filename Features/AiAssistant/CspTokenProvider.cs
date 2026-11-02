using System.Text.Json.Serialization;

namespace Hub.Features.AiAssistant;

/// <summary>
/// Exchanges this pod's Kubernetes service-account token (RFC 8693) for a short-lived CSP knowledge-base token.
/// Development may use client credentials instead. Failure is a null token, never an exception.
/// </summary>
public sealed class CspTokenProvider(
    HttpClient httpClient,
    ServiceAuthOptions authOptions,
    CspKnowledgeOptions cspOptions,
    IHostEnvironment environment,
    TimeProvider timeProvider,
    ILogger<CspTokenProvider> logger) : IServiceTokenProvider
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HalfLifeThreshold = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan WarningInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _cacheLock = new();
    private CachedToken? _cached;
    private long _lastWarningTicks = long.MinValue;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(cspOptions.BaseUrl) && (DevSecretUsable || File.Exists(authOptions.AssertionPath));

    private bool DevSecretUsable =>
        environment.IsDevelopment() && !string.IsNullOrWhiteSpace(authOptions.ClientSecret);

    public async Task<string?> GetTokenAsync(CancellationToken ct)
    {
        if (TryGetCached(out var cached))
        {
            return cached;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (TryGetCached(out cached))
            {
                return cached;
            }

            var fetched = await RequestAsync(ct);
            if (fetched is null)
            {
                return null;
            }

            lock (_cacheLock)
            {
                _cached = fetched;
            }

            return fetched.Value;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate()
    {
        lock (_cacheLock)
        {
            _cached = null;
        }
    }

    private bool TryGetCached(out string? token)
    {
        lock (_cacheLock)
        {
            if (_cached is { } entry && timeProvider.GetUtcNow() < entry.RefreshAt)
            {
                token = entry.Value;
                return true;
            }
        }

        token = null;
        return false;
    }

    private async Task<CachedToken?> RequestAsync(CancellationToken ct)
    {
        try
        {
            var baseUrl = cspOptions.BaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                Warn("CSP URL is not configured");
                return null;
            }

            using var request = BuildRequest($"{baseUrl.TrimEnd('/')}/api/v1/knowledge/token");
            if (request is null)
            {
                return null;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(RequestTimeout);

            using var response = await httpClient.SendAsync(request, timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                Warn($"CSP token endpoint returned {(int)response.StatusCode}");
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(timeout.Token);
            if (body is null || string.IsNullOrWhiteSpace(body.AccessToken) || body.ExpiresIn <= 0)
            {
                Warn("CSP token response was missing access_token or expires_in");
                return null;
            }

            if (!string.Equals(body.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
            {
                Warn("CSP token response token_type was not Bearer");
                return null;
            }

            var lifetime = TimeSpan.FromSeconds(body.ExpiresIn);
            var refreshIn = lifetime > HalfLifeThreshold ? lifetime - RefreshMargin : lifetime / 2;
            return new CachedToken(body.AccessToken, timeProvider.GetUtcNow() + refreshIn);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Warn($"CSP token request failed ({ex.GetType().Name})");
            return null;
        }
    }

    private HttpRequestMessage? BuildRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);

        if (DevSecretUsable)
        {
            request.Content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", authOptions.ClientId),
                new KeyValuePair<string, string>("client_secret", authOptions.ClientSecret!)
            ]);
            return request;
        }

        string assertion;
        try
        {
            assertion = File.ReadAllText(authOptions.AssertionPath).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            request.Dispose();
            Warn($"Service account token file could not be read ({ex.GetType().Name})");
            return null;
        }

        if (assertion.Length == 0)
        {
            request.Dispose();
            Warn("Service account token file is empty");
            return null;
        }

        request.Content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:token-exchange"),
            new KeyValuePair<string, string>("subject_token", assertion),
            new KeyValuePair<string, string>("subject_token_type", "urn:ietf:params:oauth:token-type:jwt"),
            new KeyValuePair<string, string>("audience", "csp")
        ]);
        return request;
    }

    private void Warn(string message)
    {
        var now = timeProvider.GetUtcNow().UtcTicks;
        var last = Interlocked.Read(ref _lastWarningTicks);
        if (last != long.MinValue && now - last < WarningInterval.Ticks)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastWarningTicks, now, last) == last)
        {
            logger.LogWarning("Service token unavailable: {Reason}", message);
        }
    }

    private sealed record CachedToken(string Value, DateTimeOffset RefreshAt);

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
