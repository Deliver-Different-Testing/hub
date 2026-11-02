using System.Net;
using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hub.Tests.Features.AiAssistant;

internal sealed class FakeTime : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

internal sealed class FakeEnv(string name) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = name;
    public string ApplicationName { get; set; } = "test";
    public string ContentRootPath { get; set; } = string.Empty;
    public IFileProvider ContentRootFileProvider { get; set; } = null!;
}

internal sealed record CapturedRequest(
    HttpMethod Method, string Uri, string? ContentType, string? Authorization, string[] Form, string Body);

internal sealed class StubHandler : HttpMessageHandler
{
    private readonly List<CapturedRequest> _requests = [];

    public List<CapturedRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Async { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var isForm = request.Content?.Headers.ContentType?.MediaType == "application/x-www-form-urlencoded";
        var form = !isForm || body.Length == 0
            ? []
            : body.Split('&').Select(p => Uri.UnescapeDataString(p.Replace('+', ' '))).ToArray();
        lock (_requests)
        {
            _requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!.AbsoluteUri,
                request.Content?.Headers.ContentType?.MediaType,
                request.Headers.Authorization?.ToString(),
                form,
                body));
        }

        return Async is null ? Responder(request) : await Async(request, cancellationToken);
    }
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_entries)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }
    }
}

internal static class JsonResponse
{
    public static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
