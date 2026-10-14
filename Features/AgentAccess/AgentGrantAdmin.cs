using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Hub.Features.AgentAccess;

public sealed record AgentGrantSummary(string Id, string UserId, string ClientName, DateTimeOffset? CreatedUtc, DateTimeOffset? LastUsedUtc = null);

public enum AgentGrantRevokeResult
{
    Revoked,
    NotFound
}

// What Integration Manager shows on its Connected AI apps card and does when someone presses Revoke (phase 1.7).
// Hub owns the OAuth store on the Master database, so this is the only way anything else reaches it.
public interface IAgentGrantAdmin
{
    /// <summary>Live permanent grants on a tenant: the caller's own, or every user's when <paramref name="allUsers"/>.</summary>
    Task<IReadOnlyList<AgentGrantSummary>> ListAsync(
        int tenantId, string userId, bool allUsers, CancellationToken cancellationToken = default);

    Task<AgentGrantRevokeResult> RevokeAsync(
        string authorizationId, int tenantId, string userId, bool allUsers, CancellationToken cancellationToken = default);
}

public sealed class AgentGrantAdmin(IServiceScopeFactory scopes, IAgentGrantStore grants) : IAgentGrantAdmin
{
    public async Task<IReadOnlyList<AgentGrantSummary>> ListAsync(
        int tenantId, string userId, bool allUsers, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AgentAccessOpenIddictDbContext>();

        var rows = await Live(context, tenantId)
            .Where(a => allUsers || a.Subject == userId)
            .OrderByDescending(a => a.CreationDate)
            .Select(a => new
            {
                a.Id,
                a.Subject,
                ClientName = a.Application == null ? null : a.Application.DisplayName,
                ClientId = a.Application == null ? null : a.Application.ClientId,
                a.CreationDate,
                a.Properties
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new AgentGrantSummary(
                r.Id.ToString(), r.Subject ?? string.Empty, r.ClientName ?? r.ClientId ?? "AI app",
                r.CreationDate is { } created ? new DateTimeOffset(DateTime.SpecifyKind(created, DateTimeKind.Utc)) : null,
                LastUsed(r.Properties)))
            .ToList();
    }

    public async Task<AgentGrantRevokeResult> RevokeAsync(
        string authorizationId, int tenantId, string userId, bool allUsers, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(authorizationId, out var id) || id == Guid.Empty) return AgentGrantRevokeResult.NotFound;

        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AgentAccessOpenIddictDbContext>();

        var owned = await Live(context, tenantId)
            .AnyAsync(a => a.Id == id && (allUsers || a.Subject == userId), cancellationToken);
        if (!owned) return AgentGrantRevokeResult.NotFound;

        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var authorization = await manager.FindByIdAsync(authorizationId, cancellationToken);
        if (authorization is null) return AgentGrantRevokeResult.NotFound;

        await manager.TryRevokeAsync(authorization, cancellationToken);
        await grants.RevokeAsync(authorizationId, cancellationToken);
        return AgentGrantRevokeResult.Revoked;
    }

    private static DateTimeOffset? LastUsed(string? properties)
    {
        if (string.IsNullOrEmpty(properties)) return null;

        try
        {
            using var document = JsonDocument.Parse(properties);
            return document.RootElement.TryGetProperty("last_used_utc", out var value) && DateTimeOffset.TryParse(value.GetString(), out var parsed)
                ? parsed
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IQueryable<OpenIddictEntityFrameworkCoreAuthorization<Guid>> Live(AgentAccessOpenIddictDbContext context, int tenantId) =>
        context.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>()
            .AsNoTracking()
            .Where(a => EF.Property<int?>(a, "TenantId") == tenantId
                        && a.Status == Statuses.Valid
                        && a.Type == AuthorizationTypes.Permanent);
}
