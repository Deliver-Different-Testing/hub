using Anthropic;
using Hub.Features.AiAssistant.Tools;

namespace Hub.Features.AiAssistant;

public static class AiAssistantServiceExtensions
{
    /// <summary>
    /// Registers Auto-mate. The knowledge base is optional: an empty CspKnowledge:BaseUrl with no TenantURL pattern turns it off.
    /// A base URL that would send the service token over plain http outside Development stops startup.
    /// </summary>
    public static IServiceCollection AddAiAssistant(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var assistant = configuration.GetSection(AiAssistantOptions.SectionName).Get<AiAssistantOptions>() ?? new AiAssistantOptions();
        var serviceAuth = configuration.GetSection(ServiceAuthOptions.SectionName).Get<ServiceAuthOptions>() ?? new ServiceAuthOptions();
        var csp = new CspKnowledgeOptions
        {
            BaseUrl = CspKnowledgeOptions.Resolve(
                configuration[$"{CspKnowledgeOptions.SectionName}:BaseUrl"],
                Environment.GetEnvironmentVariable("TenantURL"))
        };

        var invalid = CspKnowledgeOptions.Validate(csp.BaseUrl, environment.IsDevelopment());
        if (invalid is not null)
        {
            throw new InvalidOperationException(invalid);
        }

        services.AddSingleton(assistant);
        services.AddSingleton(serviceAuth);
        services.AddSingleton(csp);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new AnthropicClient
        {
            ApiKey = assistant.ApiKey ?? string.Empty,
            Timeout = TimeSpan.FromSeconds(assistant.TimeoutSeconds),
            MaxRetries = assistant.MaxRetries
        });

        // No shared retry or breaker: a missing CSP must fail fast. Budgets live in CspKnowledgeClient, the breaker in CspKnowledgeCircuit.
        services.AddSingleton<CspKnowledgeCircuit>();
        // The token provider caches, so it is a singleton over a factory client rather than a transient typed client.
        services.AddHttpClient(nameof(CspTokenProvider), client => client.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
        services.AddSingleton<IServiceTokenProvider>(sp => new CspTokenProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(CspTokenProvider)),
            sp.GetRequiredService<ServiceAuthOptions>(),
            sp.GetRequiredService<CspKnowledgeOptions>(),
            sp.GetRequiredService<IHostEnvironment>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<CspTokenProvider>>()));
        services.AddHttpClient<ICspKnowledgeClient, CspKnowledgeClient>(client => client.Timeout = TimeSpan.FromSeconds(10));

        services.AddScoped<KbTurnSources>();
        services.AddScoped<IAiTool, SearchKnowledgeBaseTool>();
        services.AddScoped<IAiTool, GetArticleTool>();
        services.AddScoped<IAiAssistantService, AiAssistantService>();
        return services;
    }
}
