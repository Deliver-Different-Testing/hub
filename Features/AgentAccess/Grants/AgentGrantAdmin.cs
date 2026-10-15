using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Hub.Features.AgentAccess;

public sealed record AgentGrantSummary(string Id, string UserId, string ClientName, DateTimeOffset? CreatedUtc, DateTimeOffset? LastUsedUtc = null);

/// <summary>A newly approved connection nobody has been told about yet. Integration Manager sends the email.</summary>
public sealed record AgentConnection(string Id, string UserId, string ClientName, DateTimeOffset? CreatedUtc);

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

    /// <summary>Live grants on the tenant nobody has been notified about, oldest first, at most 50.</summary>
    Task<IReadOnlyList<AgentConnection>> ListUnnotifiedAsync(int tenantId, CancellationToken cancellationToken = default);

    /// <summary>Records that the notification for this connection was sent or deliberately held. The first outcome wins.</summary>
    Task<bool> MarkNotifiedAsync(string authorizationId, int tenantId, bool sent, CancellationToken cancellationToken = default);

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

    public async Task<IReadOnlyList<AgentConnection>> ListUnnotifiedAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AgentAccessOpenIddictDbContext>();

        var rows = await Live(context, tenantId)
            .Where(a => a.Properties == null || !a.Properties.Contains("notified_utc"))
            .OrderBy(a => a.CreationDate)
            .Take(50)
            .Select(a => new
            {
                a.Id,
                a.Subject,
                ClientName = a.Application == null ? null : a.Application.DisplayName,
                ClientId = a.Application == null ? null : a.Application.ClientId,
                a.CreationDate
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new AgentConnection(
                r.Id.ToString(), r.Subject ?? string.Empty, r.ClientName ?? r.ClientId ?? "AI app",
                r.CreationDate is { } created ? new DateTimeOffset(DateTime.SpecifyKind(created, DateTimeKind.Utc)) : null))
            .ToList();
    }

    public async Task<bool> MarkNotifiedAsync(string authorizationId, int tenantId, bool sent, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(authorizationId, out var id) || id == Guid.Empty) return false;

        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AgentAccessOpenIddictDbContext>();
        if (!await Live(context, tenantId).AnyAsync(a => a.Id == id, cancellationToken)) return false;

        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var authorization = await manager.FindByIdAsync(authorizationId, cancellationToken);
        if (authorization is null) return false;

        var properties = await manager.GetPropertiesAsync(authorization, cancellationToken);
        if (properties.ContainsKey("notified_utc")) return true;

        var descriptor = new OpenIddictAuthorizationDescriptor();
        await manager.PopulateAsync(descriptor, authorization, cancellationToken);
        descriptor.Properties["notified_utc"] = JsonSerializer.SerializeToElement(DateTimeOffset.UtcNow.ToString("O"));
        descriptor.Properties["notification"] = JsonSerializer.SerializeToElement(sent ? "sent" : "held");
        await manager.UpdateAsync(authorization, descriptor, cancellationToken);
        return true;
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
