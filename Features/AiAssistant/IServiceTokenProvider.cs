namespace Hub.Features.AiAssistant;

public interface IServiceTokenProvider
{
    bool IsConfigured { get; }

    Task<string?> GetTokenAsync(CancellationToken ct);

    void Invalidate();
}
