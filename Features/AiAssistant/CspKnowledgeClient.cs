using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Hub.Features.AiAssistant;

public sealed class CspKnowledgeClient(
    HttpClient httpClient,
    CspKnowledgeOptions options,
    IServiceTokenProvider tokenProvider,
    CspKnowledgeCircuit circuit) : ICspKnowledgeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(options.BaseUrl) && tokenProvider.IsConfigured;

    public async Task<CspKbSearchResult> SearchAsync(string query, int limit, TimeSpan timeout, CancellationToken ct)
    {
        if (!IsConfigured || circuit.IsOpen)
        {
            return CspKbSearchResult.Unavailable;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return new CspKbSearchResult(CspKbStatus.Ok, []);
        }

        var path = $"api/v1/knowledge/search?q={Uri.EscapeDataString(query)}&limit={limit}";
        var call = await GetAsync<List<CspKbHit>>(path, timeout, ct);

        return call.Outcome switch
        {
            Outcome.Ok => new CspKbSearchResult(CspKbStatus.Ok, call.Body ?? []),
            _ => CspKbSearchResult.Unavailable
        };
    }

    public async Task<CspKbArticleResult> GetArticleAsync(Guid id, TimeSpan timeout, CancellationToken ct)
    {
        if (!IsConfigured || circuit.IsOpen)
        {
            return CspKbArticleResult.Unavailable;
        }

        var call = await GetAsync<CspKbArticle>($"api/v1/knowledge/articles/{id}", timeout, ct, notFoundIsAbsence: true);

        return call.Outcome switch
        {
            Outcome.Ok when call.Body is not null => new CspKbArticleResult(CspKbStatus.Ok, call.Body),
            Outcome.NotFound => new CspKbArticleResult(CspKbStatus.NotFound, null),
            _ => CspKbArticleResult.Unavailable
        };
    }

    private async Task<(Outcome Outcome, T? Body)> GetAsync<T>(
        string path, TimeSpan timeout, CancellationToken ct, bool notFoundIsAbsence = false) where T : class
    {
        var endpoint = path.Split('?')[0];
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(timeout);

        try
        {
            var url = $"{options.BaseUrl!.TrimEnd('/')}/{path}";

            var token = await tokenProvider.GetTokenAsync(budget.Token);
            if (token is null)
            {
                circuit.RecordFailure($"{endpoint} no service token", permanent: false);
                return (Outcome.Failed, null);
            }

            var response = await SendAsync(url, token, budget.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                tokenProvider.Invalidate();
                token = await tokenProvider.GetTokenAsync(budget.Token);
                if (token is null)
                {
                    circuit.RecordFailure($"{endpoint} no service token", permanent: false);
                    return (Outcome.Failed, null);
                }

                response = await SendAsync(url, token, budget.Token);
            }

            using var _ = response;

            if (response.StatusCode == HttpStatusCode.NotFound && notFoundIsAbsence)
            {
                return (Outcome.NotFound, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                var permanent = response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
                circuit.RecordFailure($"{endpoint} returned {(int)response.StatusCode}", permanent);
                return (Outcome.Failed, null);
            }

            var body = await response.Content.ReadFromJsonAsync<T>(JsonOptions, budget.Token);
            circuit.RecordSuccess();
            return (Outcome.Ok, body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            circuit.RecordFailure($"{endpoint} {ex.GetType().Name}", permanent: false);
            return (Outcome.Failed, null);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string url, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await httpClient.SendAsync(request, ct);
    }

    private enum Outcome
    {
        Ok,
        NotFound,
        Failed
    }
}
