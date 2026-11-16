using System.Security.Claims;
using Hub.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace Hub.Services;

/// <param name="Interval">How long a person's branch scope is trusted before it is read again. Zero or less reads every time.</param>
public sealed record BranchClaimRefreshOptions(TimeSpan Interval)
{
    public static readonly BranchClaimRefreshOptions Default = new(TimeSpan.FromMinutes(5));
}

/// <summary>Reads a contact's current branch scope from their tenant database, outside the request's own database scope.</summary>
public interface IContactBranchReader
{
    /// <returns>The branch ids (empty = no scope), or null when the contact is gone or inactive. Throws on any read failure.</returns>
    Task<IReadOnlyList<int>?> ReadAsync(string tenantConnection, int contactId, CancellationToken cancellationToken = default);
}

// A separate scope on purpose: the request's own DynamicDespatchDbContext binds to a tenant connection the first time it is
// used, and a tenant switch sets a different connection later in the same request. Touching it here would pin it to the
// wrong tenant.
public sealed class ContactBranchReader(IServiceScopeFactory scopes) : IContactBranchReader
{
    public async Task<IReadOnlyList<int>?> ReadAsync(string tenantConnection, int contactId, CancellationToken cancellationToken = default)
    {
        var credentials = Environment.GetEnvironmentVariable("SQLCredentials");
        if (string.IsNullOrEmpty(credentials))
        {
            throw new InvalidOperationException("Could not find an environment variable named 'SQLCredentials'.");
        }

        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IConnectionStringManager>().SetConnectionString(tenantConnection + credentials);
        return await scope.ServiceProvider.GetRequiredService<IDespatchRepository>().GetContactBranchIdsAsync(contactId);
    }
}

// The BranchIds claim is written at sign-in, but a cookie lives for 12 hours (7 days with Remember me) and slides while it
// is used. Without this, a person an admin has just limited to a branch keeps seeing every branch until they sign in again,
// and so does everybody who was already signed in when the claim first shipped (no claim = no scope). Run on each
// authenticated request, it re-reads the person's tblContactBranch rows at most once per Interval and, when they differ from
// the cookie, hands back a rebuilt principal for the cookie to be re-issued with.
//
// Fails safe: any failure to read (or a contact that is gone or inactive) keeps the principal exactly as it was, so an
// error can never turn a limited person into an unlimited one. Only tenant staff are touched.
public sealed class BranchClaimRefresher(IContactBranchReader reader, IMemoryCache cache, BranchClaimRefreshOptions options)
{
    // After a failed read, wait this long before asking the database again so an outage is not hit on every request.
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(30);

    private sealed record Scope(string? Value);

    /// <returns>A rebuilt principal when the branch scope changed (or the cookie has none yet); null to leave it alone.</returns>
    public async Task<ClaimsPrincipal?> RefreshAsync(ClaimsPrincipal? principal, CancellationToken cancellationToken = default)
    {
        try
        {
            if (principal?.Identity is not ClaimsIdentity { IsAuthenticated: true } identity) return null;

            var clientType = int.TryParse(principal.FindFirst("ClientTypeId")?.Value, out var type) ? type : (int?)null;
            if (!BranchClaim.IsLimitable(clientType, Flag(principal, "IsCourier"))) return null;

            var connection = principal.FindFirst("Connection")?.Value;
            if (!int.TryParse(principal.FindFirst("ContactID")?.Value, out var contactId) || contactId <= 0
                || !int.TryParse(principal.FindFirst("CurrentTenantID")?.Value, out var tenantId)
                || string.IsNullOrEmpty(connection))
            {
                return null;
            }

            var current = await CurrentAsync(tenantId, contactId, connection, cancellationToken);
            if (current is null) return null;

            var existing = principal.FindFirst(BranchClaim.Type)?.Value;
            if (existing == current) return null;

            var claims = identity.Claims.Where(c => c.Type != BranchClaim.Type).Append(new Claim(BranchClaim.Type, current));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, identity.AuthenticationType, identity.NameClaimType, identity.RoleClaimType));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Could not refresh the branch scope; the signed-in principal is left as it was");
            return null;
        }
    }

    /// <summary>
    /// Called at every sign-in with the scope that was just read live and written into the cookie. Sign-in reads
    /// tblContactBranch itself, so without this the cache could still hold an OLDER read (an unrestricted person who was
    /// limited since, then signs in again): the next request would see the cookie differ from the cache and re-issue the
    /// cookie with the older value, undoing the fresh sign-in and, in that case, widening access for up to one interval.
    /// Replacing the entry keeps the cache and the newest cookie in step, and also covers a tenant switch (A to B to A).
    /// </summary>
    public void Seed(int tenantId, int contactId, string? claimValue)
    {
        if (options.Interval <= TimeSpan.Zero || claimValue is null) return;
        cache.Set(Key(tenantId, contactId), new Scope(claimValue), options.Interval);
    }

    private static string Key(int tenantId, int contactId) => $"hub:branchscope:{tenantId}:{contactId}";

    private async Task<string?> CurrentAsync(int tenantId, int contactId, string connection, CancellationToken cancellationToken)
    {
        var key = Key(tenantId, contactId);
        if (options.Interval > TimeSpan.Zero && cache.TryGetValue(key, out Scope? hit) && hit is not null) return hit.Value;

        string? value = null;
        try
        {
            var ids = await reader.ReadAsync(connection, contactId, cancellationToken);
            value = ids is null ? null : BranchClaim.Format(ids);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Could not read the branch scope of contact {ContactId} in tenant {TenantId}; keeping the signed-in scope",
                contactId, tenantId);
        }

        if (options.Interval > TimeSpan.Zero)
        {
            cache.Set(key, new Scope(value), value is null ? FailureBackoff : options.Interval);
        }

        return value;
    }

    private static bool Flag(ClaimsPrincipal principal, string type) =>
        string.Equals(principal.FindFirst(type)?.Value, "True", StringComparison.OrdinalIgnoreCase);
}
