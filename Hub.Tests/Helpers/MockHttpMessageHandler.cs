using System.Net;

namespace Hub.Tests.Helpers;

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpResponseMessage _response;

    private MockHttpMessageHandler(string jsonContent, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        _response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json")
        };

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(_response);

    private static HttpClient CreateClient(string jsonContent, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new MockHttpMessageHandler(jsonContent, statusCode);
        return new HttpClient(handler);
    }

    public static HttpClient CreateReCaptchaClient(bool success = true, double score = 0.9)
    {
        var json =
            $$"""{"success":{{success.ToString().ToLower()}},"score":{{score}},"action":"submit","challenge_ts":"2026-01-01T00:00:00Z","hostname":"test.com"}""";
        return CreateClient(json);
    }
}