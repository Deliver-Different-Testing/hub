using System.Text.Json;
using Hub.Features.AgentAccess.Oauth;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using Serilog;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Hub.Features.AgentAccess.Grants;

public sealed record AgentGrantSummary(string Id, string UserId, string ClientName, DateTimeOffset? CreatedUtc, DateTimeOffset? LastUsedUtc = null, int? ContactId = null);

/// <summary>A newly approved connection nobody has been told about yet. Integration Manager sends the email.</summary>
public sealed record AgentConnection(string Id, string UserId, string ClientName, DateTimeOffset? CreatedUtc);

public enum AgentGrantRevokeResult
{
    Revoked,
    NotFound,

    /// <summary>The authorization is still live: the revoke did not take effect, so nothing was reported as done.</summary>
    Failed,

    /// <summary>The authorization is revoked but its liveness key could not be removed, so apps may honour the pass until the key expires. Repeating the revoke finishes the job.</summary>
    RevokedPendingPropagation
}

// What Integration Manager shows on its Connected AI apps card and does when someone presses Revoke (phase 1.7).
// Hub owns the OAuth store on the Master database, so this is the only way anything else reaches it.
public interface IAgentGrantAdmin
{
    /// <summary>Live permanent grants on a tenant: the caller's own, or every user's when <paramref name="allUsers"/>.</summary>
    Task<IReadOnlyList<AgentGrantSummary>> ListAsync(
        int tenantId, string userId, bool allUsers, CancellationToken cancellationToken = default);

    /// <summary>The newest live grants on the tenant (every user's), at most <paramref name="limit"/>, newest first.</summary>
    Task<IReadOnlyList<AgentGrantSummary>> ListNewestAsync(int tenantId, int limit, CancellationToken cancellationToken = default);

    /// <summary>Live grants on the tenant nobody has been notified about, oldest first, at most 50.</summary>
    Task<IReadOnlyList<AgentConnection>> ListUnnotifiedAsync(int tenantId, CancellationToken cancellationToken = default);

    /// <summary>Records that the notification for this connection was sent or deliberately held. The first outcome wins.</summary>
    Task<bool> MarkNotifiedAsync(string authorizationId, int tenantId, bool sent, CancellationToken cancellationToken = default);

    Task<AgentGrantRevokeResult> RevokeAsync(
        string authorizationId, int tenantId, string userId, bool allUsers, CancellationToken cancellationToken = default);
}

public sealed class AgentGrantAdmin(IServiceScopeFactory scopes, IAgentGrantStore grants) : IAgentGrantAdmin
{
    public Task<IReadOnlyList<AgentGrantSummary>> ListAsync(
        int tenantId, string userId, bool allUsers, CancellationToken cancellationToken = default) =>
        QueryAsync(tenantId, userId, allUsers, null, cancellationToken);

    public Task<IReadOnlyList<AgentGrantSummary>> ListNewestAsync(int tenantId, int limit, CancellationToken cancellationToken = default) =>
        QueryAsync(tenantId, string.Empty, true, limit, cancellationToken);

    private async Task<IReadOnlyList<AgentGrantSummary>> QueryAsync(
        int tenantId, string userId, bool allUsers, int? limit, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AgentAccessOpenIddictDbContext>();

        var rows = await Live(context, tenantId)
            .Where(a => allUsers || a.Subject == userId)
            .OrderByDescending(a => a.CreationDate)
            .Take(limit ?? int.MaxValue)
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
                LastUsed(r.Properties),
                ContactIdOf(r.Properties)))
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
        if (!owned)
        {
            // A repeat of a revoke whose liveness key could not be removed: the authorization is already revoked, so
            // just finish removing the key. Scoped exactly like a live grant (tenant, owner), so nothing else is reachable.
            var alreadyRevoked = await Live(context, tenantId, Statuses.Revoked)
                .AnyAsync(a => a.Id == id && (allUsers || a.Subject == userId), cancellationToken);
            return alreadyRevoked ? await RemoveKeyAsync(authorizationId, tenantId, cancellationToken) : AgentGrantRevokeResult.NotFound;
        }

        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var authorization = await manager.FindByIdAsync(authorizationId, cancellationToken);
        if (authorization is null) return AgentGrantRevokeResult.NotFound;

        if (!await manager.TryRevokeAsync(authorization, cancellationToken))
        {
            // A concurrent update can lose the revoke. Only report success if the authorization really is revoked now,
            // otherwise the next refresh would carry on as if nothing happened.
            var current = await manager.FindByIdAsync(authorizationId, cancellationToken);
            if (current is null) return AgentGrantRevokeResult.NotFound;
            if (!string.Equals(await manager.GetStatusAsync(current, cancellationToken), Statuses.Revoked, StringComparison.Ordinal))
            {
                Log.Warning("Agent access revoke of grant {GrantId} on tenant {TenantId} did not take effect", authorizationId, tenantId);
                return AgentGrantRevokeResult.Failed;
            }
        }

        return await RemoveKeyAsync(authorizationId, tenantId, cancellationToken);
    }

    // The authorization is the source of truth and is revoked. Refresh and token exchange refuse a non-valid
    // authorization before they re-set the key, but apps keep honouring an existing key until it expires (up to 30
    // minutes), so a failed removal is reported distinctly and the caller can repeat the revoke.
    private async Task<AgentGrantRevokeResult> RemoveKeyAsync(string authorizationId, int tenantId, CancellationToken cancellationToken)
    {
        try
        {
            await grants.RevokeAsync(authorizationId, cancellationToken);
            return AgentGrantRevokeResult.Revoked;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Agent access grant {GrantId} on tenant {TenantId} is revoked but its liveness key could not be removed", authorizationId, tenantId);
            return AgentGrantRevokeResult.RevokedPendingPropagation;
        }
    }

    private static int? ContactIdOf(string? properties)
    {
        if (string.IsNullOrEmpty(properties)) return null;

        try
        {
            using var document = JsonDocument.Parse(properties);
            return document.RootElement.TryGetProperty("contact_id", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var id)
                ? id
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
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

    private static IQueryable<OpenIddictEntityFrameworkCoreAuthorization<Guid>> Live(
        AgentAccessOpenIddictDbContext context, int tenantId, string status = Statuses.Valid) =>
        context.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>()
            .AsNoTracking()
            .Where(a => EF.Property<int?>(a, "TenantId") == tenantId
                        && a.Status == status
                        && a.Type == AuthorizationTypes.Permanent);
}
